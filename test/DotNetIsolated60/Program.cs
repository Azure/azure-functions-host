using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;

//Debugger.Launch();

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureGeneratedFunctionMetadataProvider()
    .Build();

host.Run();
