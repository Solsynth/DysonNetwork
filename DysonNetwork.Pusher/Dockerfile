﻿FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

RUN apt-get update && apt-get install -y --no-install-recommends \
    libkrb5-dev

USER $APP_UID

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DysonNetwork.Pusher/DysonNetwork.Pusher.csproj", "DysonNetwork.Pusher/"]
RUN dotnet restore "DysonNetwork.Pusher/DysonNetwork.Pusher.csproj"
COPY . .
WORKDIR "/src/DysonNetwork.Pusher"
RUN dotnet build "./DysonNetwork.Pusher.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./DysonNetwork.Pusher.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DysonNetwork.Pusher.dll"]
