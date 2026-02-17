using Microsoft.Extensions.Hosting;

var hostBuilder = new HostBuilder();

hostBuilder.ConfigureFunctionsWebApplication();

var host = hostBuilder.Build();
host.Run();
