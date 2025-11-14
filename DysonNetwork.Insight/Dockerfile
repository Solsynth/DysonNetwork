#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DysonNetwork.Insight/DysonNetwork.Insight.csproj", "DysonNetwork.Insight/"]
COPY ["DysonNetwork.Shared/DysonNetwork.Shared.csproj", "DysonNetwork.Shared/"]
COPY ["DysonNetwork.Develop/DysonNetwork.Develop.csproj", "DysonNetwork.Develop/"]
RUN dotnet restore "DysonNetwork.Insight/DysonNetwork.Insight.csproj"
COPY . .
WORKDIR "/src/DysonNetwork.Insight"
RUN dotnet build "DysonNetwork.Insight.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "DysonNetwork.Insight.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DysonNetwork.Insight.dll"]
