using InstagramEmbed.Application.Services;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.Encoder =
            System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping);

builder.Services.AddMemoryCache(o =>
{

    o.SizeLimit = 5_000;
});

builder.Services.AddHttpClient("regular", c =>
{
    c.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0 Safari/537.36");
    c.Timeout = TimeSpan.FromMinutes(5);
});

builder.Services.AddHttpClient("snapsave", c =>
{
    c.Timeout = TimeSpan.FromSeconds(20);
});

builder.Services.AddSingleton<PostCacheService>();
builder.Services.AddSingleton<DonateMessageService>();

builder.Services.AddHostedService<SnapSaveProcessService>();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.MapControllers();

app.Run();