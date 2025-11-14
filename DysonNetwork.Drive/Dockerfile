FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Stage 1: Install runtime dependencies

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

# Stage 2: Build .NET application
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DysonNetwork.Drive/DysonNetwork.Drive.csproj", "DysonNetwork.Drive/"]
RUN dotnet restore "DysonNetwork.Drive/DysonNetwork.Drive.csproj"
COPY . .

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
