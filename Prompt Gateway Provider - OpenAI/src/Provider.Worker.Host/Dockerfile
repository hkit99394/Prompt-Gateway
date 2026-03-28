# Provider Worker (OpenAI) - Dockerfile for ECS Fargate

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["Provider.Worker.Host/Provider.Worker.Host.csproj", "Provider.Worker.Host/"]
COPY ["Provider.Worker/Provider.Worker.csproj", "Provider.Worker/"]
COPY ["Provider.Worker.Aws/Provider.Worker.Aws.csproj", "Provider.Worker.Aws/"]

RUN dotnet restore "Provider.Worker.Host/Provider.Worker.Host.csproj"

COPY . .
RUN dotnet build "Provider.Worker.Host/Provider.Worker.Host.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Provider.Worker.Host/Provider.Worker.Host.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Provider.Worker.Host.dll"]
