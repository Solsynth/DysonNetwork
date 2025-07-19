# -------- Base runtime image --------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base

# Use non-root user if defined in Docker run
USER ${APP_UID:-1000}

WORKDIR /app
EXPOSE 8080 8081

# -------- Build stage --------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy only the necessary project files first for better layer caching
COPY ["DysonNetwork.Sphere/DysonNetwork.Sphere.csproj", "DysonNetwork.Sphere/"]
COPY ["DysonNetwork.Shared/DysonNetwork.Shared.csproj", "DysonNetwork.Shared/"]

# Restore NuGet packages
RUN dotnet restore "DysonNetwork.Sphere/DysonNetwork.Sphere.csproj"

# Copy the rest of the source code
COPY . .

# Build the project (this will skip frontend build as it's not needed)
WORKDIR "/src/DysonNetwork.Sphere"
RUN dotnet build -c $BUILD_CONFIGURATION -o /app/build \
    --no-restore \
    -p:TypeScriptCompileBlocked=true \
    -p:UseRazorBuildServer=false

# -------- Publish stage --------
FROM build AS publish
ARG BUILD_CONFIGURATION=Release

# Publish the application without frontend assets
RUN dotnet publish "DysonNetwork.Sphere.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    --no-build \
    -p:PublishReadyToRun=true \
    -p:TieredPGO=true \
    -p:SuppressTrimAnalysisWarnings=true \
    -p:TypeScriptCompileBlocked=true \
    -p:UseRazorBuildServer=false

# -------- Final image --------
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "DysonNetwork.Sphere.dll"]