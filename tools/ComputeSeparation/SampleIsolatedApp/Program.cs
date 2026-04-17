using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// The worker SDK binds its HTTP listener to localhost:{random_port}, which only accepts
// loopback traffic. In production (same pod) this works because containers share a
// network namespace. In Aspire dev harness (separate containers) we need an additional
// listener on all interfaces so the proxy can reach us across the Docker network.
string? httpPort = Environment.GetEnvironmentVariable("FUNCTIONS_HTTP_PORT");
if (!string.IsNullOrEmpty(httpPort))
{
    builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
    {
        options.ListenAnyIP(int.Parse(httpPort));
    });
}

var host = builder.Build();
host.Run();
