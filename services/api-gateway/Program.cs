using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OrderPlatform.Contracts;
using Yarp.ReverseProxy.Transforms;

// API Gateway: dış dünyanın gördüğü TEK adres.
//   İstemci → Gateway (:8000) → order / inventory / payment / notification servisleri
//
// Gateway'in işleri:
//   1) Kimlik doğrulama: Keycloak'ın verdiği JWT'yi doğrular.
//   2) Kimlik aktarımı: token'daki "sub" değerini X-Customer-Id başlığıyla servislere iletir.
//      İstemcinin kendi gönderdiği X-Customer-Id başlığı SİLİNİR (sahte kimlik engeli).
//   3) Rate limiting: kullanıcı başına belirli sürede belirli sayıda istek.
//   4) Yönlendirme: /api/orders/... → order-service/orders/... (YARP, appsettings.json → ReverseProxy)

var builder = WebApplication.CreateBuilder(args);
var auth = builder.Configuration.GetSection("Auth");

// ---------- 1) Kimlik doğrulama ----------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Gateway Keycloak'a Docker ağı içinden ulaşır (keycloak:8080)...
        options.Authority = auth["Authority"];
        options.RequireHttpsMetadata = false; // yerel geliştirme: HTTP

        // ...ama token'ı tarayıcı localhost:8081'den aldığı için "iss" alanı o adresi taşır.
        // Bu yüzden kabul edilen issuer'ı açıkça veriyoruz.
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = auth["ValidIssuer"],
            ValidAudience = auth["Audience"],
            NameClaimType = "preferred_username"
        };

        // Claim adlarını olduğu gibi bırak ("sub" → ClaimTypes.NameIdentifier dönüşümü yapma).
        options.MapInboundClaims = false;

        // Keycloak henüz açılmadıysa anahtarları 5 dakika değil 10 saniye sonra tekrar dene.
        options.RefreshInterval = TimeSpan.FromSeconds(10);
    });

builder.Services.AddAuthorization(options =>
    options.AddPolicy("customer", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("sub")));

// ---------- 3) Rate limiting ----------
var rateLimit = builder.Configuration.GetSection("RateLimiting");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Her kullanıcının kendi kotası var; giriş yapmamışsa IP adresine göre.
    options.AddPolicy("per-user", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.User.FindFirst("sub")?.Value
                      ?? context.Connection.RemoteIpAddress?.ToString()
                      ?? "anonymous",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimit.GetValue("PermitLimit", 20),
            Window = TimeSpan.FromSeconds(rateLimit.GetValue("WindowSeconds", 10)),
            QueueLimit = 0
        }));
});

// Frontend (http://localhost:5173) tarayıcıdan doğrudan gateway'e istek atabilsin.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("Location", PlatformHeaders.IdempotentReplayed)));

// ---------- 2) + 4) Yönlendirme ve kimlik aktarımı ----------
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(context =>
    {
        // İstemci "X-Customer-Id: <başkasının id'si>" gönderse bile servise ulaşmaz.
        context.AddRequestHeaderRemove(PlatformHeaders.CustomerId);

        // Yerine doğrulanmış token'daki kimlik yazılır.
        context.AddRequestTransform(transform =>
        {
            var customerId = transform.HttpContext.User.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(customerId))
                transform.ProxyRequest.Headers.TryAddWithoutValidation(PlatformHeaders.CustomerId, customerId);
            return ValueTask.CompletedTask;
        });
    });

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health");
app.MapReverseProxy();

app.Run();
