FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5226

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["DysonNetwork.Padlock/DysonNetwork.Padlock.csproj", "DysonNetwork.Padlock/"]
COPY ["DysonNetwork.Shared/DysonNetwork.Shared.csproj", "DysonNetwork.Shared/"]
RUN dotnet restore "DysonNetwork.Padlock/DysonNetwork.Padlock.csproj"
COPY . .
WORKDIR "/src/DysonNetwork.Padlock"
RUN dotnet build "DysonNetwork.Padlock.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "DysonNetwork.Padlock.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DysonNetwork.Padlock.dll"]
