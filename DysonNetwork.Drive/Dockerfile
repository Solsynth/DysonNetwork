FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DysonNetwork.Drive/DysonNetwork.Drive.csproj", "DysonNetwork.Drive/"]
RUN dotnet restore "DysonNetwork.Drive/DysonNetwork.Drive.csproj"
COPY . .
WORKDIR "/src/DysonNetwork.Drive"
RUN dotnet build "./DysonNetwork.Drive.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./DysonNetwork.Drive.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DysonNetwork.Drive.dll"]
