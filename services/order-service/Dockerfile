# Build context: repo kökü (docker-compose.yml içinde "context: .")
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Önce yalnızca proje dosyaları: kod değişince NuGet restore katmanı cache'ten gelir
COPY Directory.Build.props ./
COPY shared/OrderPlatform.Contracts/OrderPlatform.Contracts.csproj shared/OrderPlatform.Contracts/
COPY services/order-service/OrderService.csproj services/order-service/
RUN dotnet restore services/order-service/OrderService.csproj

COPY shared/ shared/
COPY services/order-service/ services/order-service/
RUN dotnet publish services/order-service/OrderService.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "OrderService.dll"]
