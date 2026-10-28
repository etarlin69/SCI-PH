FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

ARG BUILD_CONFIGURATION=Release

WORKDIR /src

COPY ["global.json", "./"]

# Copy project files first to keep NuGet restore cached when source code changes.
COPY ["src/Presentation/SCI-PH.Web.BFF/SCI-PH.Web.BFF.csproj", "src/Presentation/SCI-PH.Web.BFF/"]
COPY ["src/Core/SCI-PH.Application/SCI-PH.Application.csproj", "src/Core/SCI-PH.Application/"]
COPY ["src/Core/SCI-PH.Domain/SCI-PH.Domain.csproj", "src/Core/SCI-PH.Domain/"]
COPY ["src/Infrastructure/SCI-PH.Persistence/SCI-PH.Persistence.csproj", "src/Infrastructure/SCI-PH.Persistence/"]
COPY ["src/Infrastructure/SCI-PH.Infrastructure/SCI-PH.Infrastructure.csproj", "src/Infrastructure/SCI-PH.Infrastructure/"]

# Also restored for linux-x64, so the published app only carries the Linux native runtimes (OpenCV headless, SkiaSharp).
RUN dotnet restore "src/Presentation/SCI-PH.Web.BFF/SCI-PH.Web.BFF.csproj" \
    && dotnet restore "src/Presentation/SCI-PH.Web.BFF/SCI-PH.Web.BFF.csproj" --runtime linux-x64

COPY . .

WORKDIR "/src/src/Presentation/SCI-PH.Web.BFF"

RUN dotnet publish "SCI-PH.Web.BFF.csproj" \
    --configuration "$BUILD_CONFIGURATION" \
    --output /app/publish \
    --runtime linux-x64 \
    --self-contained false \
    --no-restore \
    /p:UseAppHost=false


FROM build AS migrations

WORKDIR /src

RUN dotnet tool restore


# Face models of the local OpenCV provider (ADR-012): downloaded from their pinned URLs and verified by SHA-256 by the same
# script developers use; the build fails on a mismatch. FACE_MODELS=none builds an image without models (Fake/Disabled only;
# Provider=OpenCv then refuses to start). Models are never in the build context (.models/ is ignored).
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS face-models

ARG FACE_MODELS=include

COPY ["scripts/biometrics/fetch-face-models.sh", "/tmp/fetch-face-models.sh"]

RUN mkdir -p /models/face     && if [ "$FACE_MODELS" = "include" ]; then sh /tmp/fetch-face-models.sh /models/face;        elif [ "$FACE_MODELS" != "none" ]; then echo "FACE_MODELS must be include or none" >&2; exit 1; fi


FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

WORKDIR /app

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .

# Linux x64 runtime: OpenCV headless (no GTK/X11). Third-party notices travel with the binaries; keys, connection strings
# and templates never enter the image (runtime secrets only).
COPY ["docs/legal/face-opencv-third-party.md", "docs/legal/licenses/", "/app/licenses/"]
COPY --from=face-models /models/face /app/models/face

USER $APP_UID

ENTRYPOINT ["dotnet", "SCI-PH.Web.BFF.dll"]