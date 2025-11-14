# Stage 1: Base runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Stage 2: Build .NET application
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy .csproj and restore as distinct layers
COPY ["DysonNetwork.Pass/DysonNetwork.Pass.csproj", "DysonNetwork.Pass/"]
RUN dotnet restore "DysonNetwork.Pass/DysonNetwork.Pass.csproj"

# Copy everything else and build
COPY . .

# Build the application
WORKDIR "/src/DysonNetwork.Pass"
RUN dotnet build "DysonNetwork.Pass.csproj" -c Release -o /app/build \
    -p:TypeScriptCompileBlocked=true \
    -p:UseRazorBuildServer=false

# Stage 4: Publish
FROM build AS publish
RUN mkdir -p /app/publish/zh-Hans
RUN dotnet publish "DysonNetwork.Pass.csproj" -c Release -o /app/publish \
    -p:TypeScriptCompileBlocked=true \
    -p:UseRazorBuildServer=false \
    /p:UseAppHost=false

# Final stage: Runtime
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DysonNetwork.Pass.dll"]