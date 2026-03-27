# Control Plane API - Dockerfile for ECS Fargate
# Exposes port 8080 (ASPNETCORE_URLS=http://+:8080)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["ControlPlane.Api/ControlPlane.Api.csproj", "ControlPlane.Api/"]
COPY ["ControlPlane.Core/ControlPlane.Core.csproj", "ControlPlane.Core/"]
COPY ["ControlPlane.Aws/ControlPlane.Aws.csproj", "ControlPlane.Aws/"]

RUN dotnet restore "ControlPlane.Api/ControlPlane.Api.csproj"

COPY . .
RUN dotnet build "ControlPlane.Api/ControlPlane.Api.csproj" -c Release -f net10.0 -o /app/build

FROM build AS publish
RUN dotnet publish "ControlPlane.Api/ControlPlane.Api.csproj" -c Release -f net10.0 -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
# ECS container health check uses curl; aspnet runtime image does not include it
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=publish /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "ControlPlane.Api.dll"]
