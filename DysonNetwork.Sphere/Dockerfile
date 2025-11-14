# Stage 1: Base runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Stage 2: Build .NET application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# First copy only the solution and project files to restore packages
COPY ["DysonNetwork.Sphere/DysonNetwork.Sphere.csproj", "DysonNetwork.Sphere/"]
COPY ["DysonNetwork.Pass/DysonNetwork.Pass.csproj", "DysonNetwork.Pass/"]
COPY ["DysonNetwork.Shared/DysonNetwork.Shared.csproj", "DysonNetwork.Shared/"]

# Restore packages
RUN dotnet restore "DysonNetwork.Sphere/DysonNetwork.Sphere.csproj"

# Copy everything else and build
COPY . .

# Create necessary directories for localized resources
RUN mkdir -p /src/DysonNetwork.Sphere/bin/Release/net10.0/zh-hans \
    && mkdir -p /src/DysonNetwork.Pass/bin/Release/net10.0/zh-hans

# Build the application
WORKDIR "/src/DysonNetwork.Sphere"
RUN dotnet build "DysonNetwork.Sphere.csproj" -c $BUILD_CONFIGURATION -o /app/build \
    -p:TypeScriptCompileBlocked=true \
    -p:UseRazorBuildServer=false

# Stage 4: Publish
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# Ensure the target directory for localized resources exists
RUN mkdir -p /app/publish/zh-Hans
RUN dotnet publish "DysonNetwork.Sphere.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final stage: Runtime
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DysonNetwork.Sphere.dll"]
