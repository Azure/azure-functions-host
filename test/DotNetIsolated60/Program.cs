using Microsoft.Extensions.Hosting;

Console.WriteLine("Console Out from worker on startup.");

//Debugger.Launch();

// Tests can set an env var that will swap this to use the proxy
bool useProxy = Environment.GetEnvironmentVariable("UseProxyInTest")?.Contains("1") ?? false;

var hostBuilder = new HostBuilder();

if (useProxy)
{
    hostBuilder
        .ConfigureFunctionsWebApplication();
}
else
{
    hostBuilder
        .ConfigureFunctionsWorkerDefaults();
}

var host = hostBuilder.Build();
host.Run();
