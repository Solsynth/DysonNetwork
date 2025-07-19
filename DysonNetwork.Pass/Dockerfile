FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

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

# Restore NuGet packages
COPY ["DysonNetwork.Pass/DysonNetwork.Pass.csproj", "DysonNetwork.Pass/"]
RUN dotnet restore "DysonNetwork.Pass/DysonNetwork.Pass.csproj"

# Copy everything and build
COPY . .
WORKDIR "/src/DysonNetwork.Pass"
RUN dotnet build "./DysonNetwork.Pass.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./DysonNetwork.Pass.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DysonNetwork.Pass.dll"]
