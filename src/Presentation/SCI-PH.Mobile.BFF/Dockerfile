FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

ARG BUILD_CONFIGURATION=Release

WORKDIR /src

COPY ["global.json", "./"]

# Copy project files first to keep NuGet restore cached when source code changes.
COPY ["src/Presentation/SCI-PH.Mobile.BFF/SCI-PH.Mobile.BFF.csproj", "src/Presentation/SCI-PH.Mobile.BFF/"]
COPY ["src/Core/SCI-PH.Application/SCI-PH.Application.csproj", "src/Core/SCI-PH.Application/"]
COPY ["src/Core/SCI-PH.Domain/SCI-PH.Domain.csproj", "src/Core/SCI-PH.Domain/"]
COPY ["src/Infrastructure/SCI-PH.Persistence/SCI-PH.Persistence.csproj", "src/Infrastructure/SCI-PH.Persistence/"]
COPY ["src/Infrastructure/SCI-PH.Infrastructure/SCI-PH.Infrastructure.csproj", "src/Infrastructure/SCI-PH.Infrastructure/"]

RUN dotnet restore "src/Presentation/SCI-PH.Mobile.BFF/SCI-PH.Mobile.BFF.csproj"

COPY . .

WORKDIR "/src/src/Presentation/SCI-PH.Mobile.BFF"

RUN dotnet publish "SCI-PH.Mobile.BFF.csproj" \
    --configuration "$BUILD_CONFIGURATION" \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false


FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

WORKDIR /app

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .

USER $APP_UID

ENTRYPOINT ["dotnet", "SCI-PH.Mobile.BFF.dll"]
