# Stage 1: Base runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Stage 2: Build SPA
FROM node:22-alpine AS spa-builder
WORKDIR /src

# Copy package files for SPA
COPY ["DysonNetwork.Pass/Client/package.json", "DysonNetwork.Pass/Client/package-lock.json*", "./Client/"]

# Install SPA dependencies
WORKDIR /src/Client
RUN npm ci

# Copy SPA source
COPY ["DysonNetwork.Pass/Client/", "./"]

# Build SPA
RUN npm run build

# Stage 3: Build .NET application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy .csproj and restore as distinct layers
COPY ["DysonNetwork.Pass/DysonNetwork.Pass.csproj", "DysonNetwork.Pass/"]
RUN dotnet restore "DysonNetwork.Pass/DysonNetwork.Pass.csproj"

# Copy everything else and build
COPY . .

# Copy built SPA to wwwroot
COPY --from=spa-builder /src/Client/dist /src/DysonNetwork.Pass/wwwroot/dist

# Build the application
WORKDIR "/src/DysonNetwork.Pass"
RUN dotnet build "DysonNetwork.Pass.csproj" -c Release -o /app/build \
    -p:TypeScriptCompileBlocked=true \
    -p:UseRazorBuildServer=false

# Stage 4: Publish
FROM build AS publish
RUN dotnet publish "DysonNetwork.Pass.csproj" -c Release -o /app/publish \
    -p:TypeScriptCompileBlocked=true \
    -p:UseRazorBuildServer=false \
    /p:UseAppHost=false

# Final stage: Runtime
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DysonNetwork.Pass.dll"]
