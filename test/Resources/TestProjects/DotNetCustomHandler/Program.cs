var app = WebApplication.CreateBuilder(args).Build();

var port = Environment.GetEnvironmentVariable("FUNCTIONS_CUSTOMHANDLER_PORT")
           ?? throw new InvalidOperationException("FUNCTIONS_CUSTOMHANDLER_PORT environment variable is not set.");

app.Urls.Add($"http://localhost:{port}");

app.MapGet("/api/SimpleHttpTrigger", () => "Hello from .NET custom handler");

Console.WriteLine($".NET server listening on FUNCTIONS_CUSTOMHANDLER_PORT: {port}");
await app.RunAsync();
