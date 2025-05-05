var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/api/hello", () => "Hello world");

app.Run();
