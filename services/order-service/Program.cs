using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OrderService.Features.Orders;
using OrderService.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<OrderDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("OrderDb"))
    .UseSnakeCaseNamingConvention());

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
