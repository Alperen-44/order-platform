using System.Text.Json.Serialization;
using InventoryService.Features.Products;
using InventoryService.Features.Reservations;
using InventoryService.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<InventoryDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("InventoryDb"))
    .UseSnakeCaseNamingConvention());

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
