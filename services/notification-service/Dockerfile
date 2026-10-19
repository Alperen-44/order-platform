# Build context: repo kökü (docker-compose.yml içinde "context: .")
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Önce yalnızca proje dosyaları: kod değişince NuGet restore katmanı cache'ten gelir
COPY Directory.Build.props ./
COPY shared/OrderPlatform.Contracts/OrderPlatform.Contracts.csproj shared/OrderPlatform.Contracts/
COPY shared/OrderPlatform.Messaging/OrderPlatform.Messaging.csproj shared/OrderPlatform.Messaging/
COPY services/notification-service/NotificationService.csproj services/notification-service/
RUN dotnet restore services/notification-service/NotificationService.csproj

COPY shared/ shared/
COPY services/notification-service/ services/notification-service/
RUN dotnet publish services/notification-service/NotificationService.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "NotificationService.dll"]
