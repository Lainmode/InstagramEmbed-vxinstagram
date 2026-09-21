using InstagramEmbed.Application.Services;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
// This stateless app has no login cookies or persistent protected data.
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.Configure<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>(o =>
    o.XmlRepository = new MemoryKeyRepository());
builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache(o => o.SizeLimit = 32 * 1024 * 1024);
builder.Services.AddSingleton<InstanceSettings>();
builder.Services.AddSingleton<PostCacheService>();
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = 429;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
        RateLimitPartition.GetConcurrencyLimiter("instance", _ => new ConcurrencyLimiterOptions {
            PermitLimit = 32, QueueLimit = 0
        }));
});
var app = builder.Build();
// Validate settings before accepting requests.
app.Services.GetRequiredService<InstanceSettings>();
app.Services.GetRequiredService<PostCacheService>();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.MapControllers();
app.Run();
