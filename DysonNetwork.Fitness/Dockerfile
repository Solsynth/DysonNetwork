FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        libkrb5-3 \
        libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DysonNetwork.Fitness/DysonNetwork.Fitness.csproj", "DysonNetwork.Fitness/"]
RUN dotnet restore "DysonNetwork.Fitness/DysonNetwork.Fitness.csproj"
COPY . .
WORKDIR "/src/DysonNetwork.Fitness"
RUN dotnet build "./DysonNetwork.Fitness.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./DysonNetwork.Fitness.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DysonNetwork.Fitness.dll"]
