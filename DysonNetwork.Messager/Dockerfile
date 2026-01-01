# Stage 1: Base runtime image
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

# Stage 2: Build .NET application
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# First copy only the solution and project files to restore packages
COPY ["DysonNetwork.Messager/DysonNetwork.Messager.csproj", "DysonNetwork.Messager/"]
COPY ["DysonNetwork.Shared/DysonNetwork.Shared.csproj", "DysonNetwork.Shared/"]

# Restore packages
RUN dotnet restore "DysonNetwork.Messager/DysonNetwork.Messager.csproj"

# Copy everything else and build
COPY . .

# Build the application
WORKDIR "/src/DysonNetwork.Messager"
RUN dotnet build "DysonNetwork.Messager.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Stage 4: Publish
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "DysonNetwork.Messager.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final stage: Runtime
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DysonNetwork.Messager.dll"]
