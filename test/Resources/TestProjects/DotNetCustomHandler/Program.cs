var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/api/SimpleHttpTrigger", (HttpContext context) => "Hello from .NET custom handler");

var customHandlerPort = Environment.GetEnvironmentVariable("FUNCTIONS_CUSTOMHANDLER_PORT");
if (!string.IsNullOrEmpty(customHandlerPort))
{
    Console.WriteLine($"FUNCTIONS_CUSTOMHANDLER_PORT: {customHandlerPort}");
    app.Urls.Add($"http://localhost:{customHandlerPort}");
}
else
{
    throw new InvalidOperationException("FUNCTIONS_CUSTOMHANDLER_PORT environment variable is not set.");
}

Console.WriteLine($".NET server Listening...on FUNCTIONS_CUSTOMHANDLER_PORT: {customHandlerPort}");
app.Run();
