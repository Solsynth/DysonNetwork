FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Install only necessary dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
  libfontconfig1 \
  libfreetype6 \
  libpng-dev \
  libharfbuzz0b \
  libgif7 \
  libvips \
  ffmpeg \
  && apt-get clean \
  && rm -rf /var/lib/apt/lists/* \
    
USER $APP_UID

# Stage 2: Build SPA
FROM node:22-alpine AS spa-builder
WORKDIR /src

# Copy package files for SPA
COPY ["DysonNetwork.Drive/Client/package.json", "DysonNetwork.Drive/Client/package-lock.json*", "./Client/"]

# Install SPA dependencies
WORKDIR /src/Client
RUN npm install

# Copy SPA source
COPY ["DysonNetwork.Drive/Client/", "./"]

# Build SPA
RUN npm run build

# Stage 3: Build .NET application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DysonNetwork.Drive/DysonNetwork.Drive.csproj", "DysonNetwork.Drive/"]
RUN dotnet restore "DysonNetwork.Drive/DysonNetwork.Drive.csproj"
COPY . .

# Copy built SPA to wwwroot
COPY --from=spa-builder /src/Client/dist /src/DysonNetwork.Drive/wwwroot/dist

WORKDIR "/src/DysonNetwork.Drive"
RUN dotnet build "./DysonNetwork.Drive.csproj" -c $BUILD_CONFIGURATION -o /app/build \
    -p:TypeScriptCompileBlocked=true \
    -p:UseRazorBuildServer=false

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./DysonNetwork.Drive.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DysonNetwork.Drive.dll"]
