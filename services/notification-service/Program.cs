using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.EntityFrameworkCore;
using NotificationService.Features;
using NotificationService.Messaging;
using NotificationService.Persistence;
using OrderPlatform.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<NotificationDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("NotificationDb"))
    .UseSnakeCaseNamingConvention());

// Mesajlaşma: yalnızca dinliyor. Producer, işlenemeyen mesajları DLQ'ya yazmak için gerekli.
builder.Services.AddKafka(builder.Configuration);
builder.Services.AddHostedService<NotificationConsumer>();

// Türkçe karakterler JSON'da ş gibi kaçış dizisi olarak değil, olduğu gibi görünsün.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All));

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
app.MapNotificationEndpoints();

app.Run();
