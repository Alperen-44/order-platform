using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OrderPlatform.Contracts;
using OrderPlatform.Messaging;
using OrderService.Features.Orders;
using OrderService.Messaging;
using OrderService.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<OrderDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("OrderDb"))
    .UseSnakeCaseNamingConvention());

// Mesajlaşma: outbox'taki olayları order.events'e gönder, inventory.events'i dinle.
builder.Services.AddKafka(builder.Configuration);
builder.Services.AddOutboxRelay<OrderDbContext>(Topics.OrderEvents);
builder.Services.AddHostedService<InventoryEventsConsumer>();

// Enum'lar JSON'da sayı değil metin olarak görünsün: "status": "Pending"
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
app.MapOrderEndpoints();

app.Run();
