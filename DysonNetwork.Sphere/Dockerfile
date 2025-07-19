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

# Install Node.js 22 and npm
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get -y install --no-install-recommends curl ca-certificates \
    && curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
    && apt-get -y install --no-install-recommends nodejs \
    && npm install -g npm \
    && rm -rf /var/lib/apt/lists/*

# Copy everything and build
COPY . .
WORKDIR /src/DysonNetwork.Sphere
RUN dotnet restore
RUN dotnet build -c $BUILD_CONFIGURATION -o /app/build

# -------- Publish stage --------
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN mkdir -p /app/publish/zh-Hans

# Trim, ReadyToRun, single-file (optional), no self-contained to keep image small
RUN dotnet publish -c $BUILD_CONFIGURATION -o /app/publish \
  -p:PublishReadyToRun=true \
  -p:TieredPGO=true \
  -p:SuppressTrimAnalysisWarnings=true

# -------- Final image --------
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "DysonNetwork.Sphere.dll"]