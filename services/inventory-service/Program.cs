using System.Text.Json.Serialization;
using InventoryService.Features.Products;
using InventoryService.Features.Reservations;
using InventoryService.Messaging;
using InventoryService.Persistence;
using Microsoft.EntityFrameworkCore;
using OrderPlatform.Contracts;
using OrderPlatform.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<InventoryDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("InventoryDb"))
    .UseSnakeCaseNamingConvention());

builder.Services.AddScoped<StockReservationService>();

// Mesajlaşma: order.events dinle (rezerve et / kesinleştir / geri bırak), sonuçları inventory.events'e yayınla.
builder.Services.AddKafka(builder.Configuration);
builder.Services.AddOutboxRelay<InventoryDbContext>(Topics.InventoryEvents);
builder.Services.AddHostedService<OrderEventsConsumer>();

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health");
app.MapProductEndpoints();
app.MapReservationEndpoints();

app.Run();
