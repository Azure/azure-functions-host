using Microsoft.Extensions.Hosting;

Console.WriteLine("Console Out from worker on startup.");

var hostBuilder = new HostBuilder();

hostBuilder.ConfigureFunctionsWebApplication();

var host = hostBuilder.Build();
host.Run();
