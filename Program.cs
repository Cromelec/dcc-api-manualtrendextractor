using WebAPIDateTrendSelector.Hubs;
using WebAPIDateTrendSelector.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──
builder.Services.AddRazorPages();
builder.Services.AddControllers();

builder.Services.AddHttpClient("DesigoClient")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

builder.Services.AddSingleton<DesigoAuthService>();
builder.Services.AddSingleton<TrendService>();
builder.Services.AddSignalR();

var app = builder.Build();

// ── ✅ PathBase EN PREMIER ──
app.UsePathBase("/Trend");

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();
app.MapHub<TrendHub>("/trendHub");  // ✅ Accessible via /Trend/trendHub

app.Run();