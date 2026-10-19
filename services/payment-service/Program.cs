using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OrderPlatform.Contracts;
using OrderPlatform.Messaging;
using PaymentService.Features;
using PaymentService.Messaging;
using PaymentService.Persistence;
using PaymentService.Provider;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PaymentDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("PaymentDb"))
    .UseSnakeCaseNamingConvention());

builder.Services.Configure<PaymentProviderOptions>(
    builder.Configuration.GetSection(PaymentProviderOptions.SectionName));
builder.Services.AddSingleton<FakePaymentProvider>();

// Mesajlaşma: PaymentRequested / OrderCancelled dinle, sonuçları payment.events'e yayınla.
builder.Services.AddKafka(builder.Configuration);
builder.Services.AddOutboxRelay<PaymentDbContext>(Topics.PaymentEvents);
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
app.MapPaymentEndpoints();

app.Run();
