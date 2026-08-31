using Microsoft.Extensions.Hosting;

const string startupMarkerSetting = "FUNCTIONS_TEST_WORKER_STARTUP_MARKER";

string? startupMarkerPath = Environment.GetEnvironmentVariable(startupMarkerSetting);
if (startupMarkerPath is not null && !File.Exists(startupMarkerPath))
{
    string? markerDirectory = Path.GetDirectoryName(startupMarkerPath);
    if (markerDirectory is not null)
    {
        Directory.CreateDirectory(markerDirectory);
    }

    File.WriteAllText(startupMarkerPath, string.Empty);
    Thread.Sleep(Timeout.Infinite);
}

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .Build();

host.Run();
