using WorkerModel.ScaleController.Services;

await Task.Delay(TimeSpan.FromSeconds(5)); // Initial delay to allow storage emulator to start

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Azure Blob Storage for app packages (zip files)
builder.AddAzureBlobClient("blobs");

// In-memory storage for metadata (no Cosmos dependency)
builder.Services.AddSingleton<InMemoryStore>();

// Add application services
builder.Services.AddSingleton<ApplicationService>();
builder.Services.AddSingleton<RuntimeService>();
builder.Services.AddSingleton<WorkerService>();
builder.Services.AddSingleton<SpecializationOrchestrator>();

// Add storage initializer as hosted service
builder.Services.AddHostedService<StorageInitializer>();

// Add HTTP client for calling Sidecar/Runtime
builder.Services.AddHttpClient();

// Named clients that bypass Aspire's default Polly resilience pipeline (10s per-attempt / 30s total)
// because specialization and proxy forwarding can take much longer during cold start.
builder.Services.AddHttpClient("proxy", client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
}).RemoveAllLoggers()
  .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler())
  .AddStandardResilienceHandler(options =>
  {
      options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
      options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
      options.Retry.MaxRetryAttempts = 1;
      options.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
      options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
  });

builder.Services.AddHttpClient("specialization", client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
}).RemoveAllLoggers()
  .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler())
  .AddStandardResilienceHandler(options =>
  {
      options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
      options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
      options.Retry.MaxRetryAttempts = 1;
      options.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
      options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
  });

// Add controllers with proper enum serialization
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Worker Model Scale Controller",
        Version = "v1",
        Description = "Mock Scale Controller for Worker Model Prototype"
    });
});

// Add CORS for local development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Scale Controller v1");
    c.RoutePrefix = "swagger";
});

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Redirect root to the UI
app.MapGet("/", () => Results.Redirect("/index.html"));

Console.WriteLine("Scale Controller starting...");
Console.WriteLine("  Swagger UI: /swagger");
Console.WriteLine("  Web UI: /index.html");
Console.WriteLine("  (Check Aspire Dashboard for actual URLs)");

app.Run();
