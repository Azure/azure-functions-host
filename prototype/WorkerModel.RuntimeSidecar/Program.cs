using WorkerModel.RuntimeSidecar.Services;

var builder = WebApplication.CreateBuilder(args);

// Add singleton services for mount state management
builder.Services.AddSingleton<MountManager>();
builder.Services.AddSingleton<PackageDownloader>();
builder.Services.AddSingleton<SquashFsMounter>();

// Add HTTP client factory for downloading packages and SC registration
builder.Services.AddHttpClient();

// Register with Scale Controller on startup and send heartbeats
builder.Services.AddHostedService<ScaleControllerRegistration>();

// Add controllers for HTTP endpoints
builder.Services.AddControllers();

var app = builder.Build();

// Map controller routes
app.MapControllers();

// Inline health check endpoints (lightweight, no controller overhead)
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/ready", (MountManager mountManager) =>
{
    var info = mountManager.GetMountInfo();
    if (info is null)
    {
        // No mount requested yet - sidecar is ready to receive /mount
        return Results.Ok(new { status = "ready", mounted = false });
    }

    if (info.IsReady)
    {
        return Results.Ok(new { status = "ready", mounted = true, mountPoint = info.MountPoint });
    }

    return Results.Json(
        new { status = "not_ready", mounted = false, state = info.State.ToString() },
        statusCode: 503);
});

Console.WriteLine("[RuntimeSidecar] Starting Runtime Sidecar...");
Console.WriteLine($"[RuntimeSidecar] Cache path: {builder.Configuration["RuntimeSidecar:CachePath"] ?? "/var/cache/functions"}");
Console.WriteLine($"[RuntimeSidecar] Default mount point: {builder.Configuration["RuntimeSidecar:DefaultMountPoint"] ?? "/home/site/wwwroot"}");

app.Run();
