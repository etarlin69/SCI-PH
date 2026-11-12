# ============================================================
# SCI-PH.Device.API
# Multi-stage Docker build
# ============================================================


# ------------------------------------------------------------
# Stage 1 - Build
# ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

ARG BUILD_CONFIGURATION=Release

WORKDIR /src


# Preserve the SDK policy defined by the repository.
COPY ["global.json", "./"]


# Copy project files first so NuGet restore can be cached.
COPY ["src/Presentation/SCI-PH.Device.API/SCI-PH.Device.API.csproj", "src/Presentation/SCI-PH.Device.API/"]

COPY ["src/Core/SCI-PH.Application/SCI-PH.Application.csproj", "src/Core/SCI-PH.Application/"]
COPY ["src/Core/SCI-PH.Domain/SCI-PH.Domain.csproj", "src/Core/SCI-PH.Domain/"]

COPY ["src/Infrastructure/SCI-PH.Persistence/SCI-PH.Persistence.csproj", "src/Infrastructure/SCI-PH.Persistence/"]
COPY ["src/Infrastructure/SCI-PH.Infrastructure/SCI-PH.Infrastructure.csproj", "src/Infrastructure/SCI-PH.Infrastructure/"]


RUN dotnet restore "src/Presentation/SCI-PH.Device.API/SCI-PH.Device.API.csproj"


# Copy the remaining repository source after restore.
COPY . .


WORKDIR "/src/src/Presentation/SCI-PH.Device.API"


RUN dotnet publish "SCI-PH.Device.API.csproj" \
    --configuration "$BUILD_CONFIGURATION" \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false


# ------------------------------------------------------------
# Stage 2 - Runtime
# ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

WORKDIR /app

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .

USER $APP_UID

ENTRYPOINT ["dotnet", "SCI-PH.Device.API.dll"]
