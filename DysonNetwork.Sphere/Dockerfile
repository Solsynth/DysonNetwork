FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
# Install Node.js
RUN apt-get update && apt-get install -y nodejs npm

ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DysonNetwork.Sphere.csproj", "DysonNetwork.Sphere/"]
RUN dotnet restore "DysonNetwork.Sphere/DysonNetwork.Sphere.csproj"
WORKDIR "/src/DysonNetwork.Sphere"
COPY . .

# Install npm packages and build Tailwind CSS
RUN npm install
RUN npm run css:build

RUN dotnet build "DysonNetwork.Sphere.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "DysonNetwork.Sphere.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DysonNetwork.Sphere.dll"]
