# Build context: repo kökü (docker-compose.yml içinde "context: .")
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY shared/OrderPlatform.Contracts/OrderPlatform.Contracts.csproj shared/OrderPlatform.Contracts/
COPY services/api-gateway/ApiGateway.csproj services/api-gateway/
RUN dotnet restore services/api-gateway/ApiGateway.csproj

COPY shared/OrderPlatform.Contracts/ shared/OrderPlatform.Contracts/
COPY services/api-gateway/ services/api-gateway/
RUN dotnet publish services/api-gateway/ApiGateway.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "ApiGateway.dll"]
