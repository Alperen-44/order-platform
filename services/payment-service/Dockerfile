# Build context: repo kökü (docker-compose.yml içinde "context: .")
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Önce yalnızca proje dosyaları: kod değişince NuGet restore katmanı cache'ten gelir
COPY Directory.Build.props ./
COPY shared/OrderPlatform.Contracts/OrderPlatform.Contracts.csproj shared/OrderPlatform.Contracts/
COPY shared/OrderPlatform.Messaging/OrderPlatform.Messaging.csproj shared/OrderPlatform.Messaging/
COPY services/payment-service/PaymentService.csproj services/payment-service/
RUN dotnet restore services/payment-service/PaymentService.csproj

COPY shared/ shared/
COPY services/payment-service/ services/payment-service/
RUN dotnet publish services/payment-service/PaymentService.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "PaymentService.dll"]
