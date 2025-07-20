FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "DysonNetwork.Pass/DysonNetwork.Pass.csproj"

# Copy SPA files
WORKDIR /src/DysonNetwork.Pass/Client
RUN npm install
RUN npm run build

# Build .NET app
WORKDIR /src
WORKDIR "/src/DysonNetwork.Pass"
RUN dotnet build "DysonNetwork.Pass.csproj" -c Release -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# Ensure the target directory for localized resources exists
RUN mkdir -p /app/publish/zh-Hans
RUN dotnet publish "DysonNetwork.Pass.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DysonNetwork.Pass.dll"]
