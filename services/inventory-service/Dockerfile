# Build context: repo kökü (docker-compose.yml içinde "context: .")
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY shared/OrderPlatform.Contracts/OrderPlatform.Contracts.csproj shared/OrderPlatform.Contracts/
COPY shared/OrderPlatform.Messaging/OrderPlatform.Messaging.csproj shared/OrderPlatform.Messaging/
COPY services/inventory-service/InventoryService.csproj services/inventory-service/
RUN dotnet restore services/inventory-service/InventoryService.csproj

COPY shared/ shared/
COPY services/inventory-service/ services/inventory-service/
RUN dotnet publish services/inventory-service/InventoryService.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "InventoryService.dll"]
