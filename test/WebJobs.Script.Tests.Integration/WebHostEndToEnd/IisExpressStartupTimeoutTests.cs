// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd;

[Trait(TestTraits.Group, TestTraits.NonE2EIisExpress)]
public class IisExpressStartupTimeoutTests
{
    private const string TestFunctionName = "IisStartupRecovery";
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(20);

    [IisExpressFact]
    public async Task WorkerStartupTimeout_StopsIisExpressProcess()
    {
        await using IisExpressTestServer server = await IisExpressTestServer.StartAsync();
        HttpStatusCode? statusCode = null;
        string responseBody = null;
        Exception requestException = null;

        using var handler = new HttpClientHandler { UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        Task<HttpResponseMessage> requestTask = client.GetAsync(new Uri(server.BaseAddress, $"api/{TestFunctionName}"));

        bool startupMarkerObserved = await server.WaitForStartupMarkerAsync(TimeSpan.FromSeconds(60));
        bool exitedWithoutTestCleanup = startupMarkerObserved && await server.WaitForExitAsync(ShutdownTimeout);

        try
        {
            using HttpResponseMessage response = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
            statusCode = response.StatusCode;
            responseBody = await response.Content.ReadAsStringAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            requestException = exception;
        }

        await server.StopAsync();

        string ancmLog = server.ReadAncmLog();
        string diagnostics = server.BuildDiagnostics(exitedWithoutTestCleanup, statusCode, responseBody, requestException, ancmLog);
        Assert.True(startupMarkerObserved, $"The isolated worker never started.{Environment.NewLine}{diagnostics}");
        Assert.True(
            ancmLog?.Contains("Starting worker process failed", StringComparison.Ordinal) == true,
            $"The expected worker startup timeout was not logged.{Environment.NewLine}{diagnostics}");
        if (!exitedWithoutTestCleanup)
        {
            Assert.Equal(HttpStatusCode.InternalServerError, statusCode);
            Assert.Contains("500.30", responseBody, StringComparison.Ordinal);
        }

        Assert.True(
            ancmLog?.Contains("Stopping JobHost", StringComparison.Ordinal) == true,
            $"The managed JobHost shutdown was not observed.{Environment.NewLine}{diagnostics}");
        Assert.True(
            exitedWithoutTestCleanup,
            $"The IIS-hosted Functions application did not terminate after the worker startup timeout.{Environment.NewLine}{diagnostics}");
    }
}

internal sealed class IisExpressTestServer : IAsyncDisposable
{
    private const string ApplicationPoolName = "IisExpressStartupTimeoutPool";
    private const string SiteName = "IisExpressStartupTimeout";
    private const string TestWorkerProjectName = "DotNetIsolatedStartupTimeout";
    private readonly string _hostRoot;
    private readonly string _startupMarkerPath;
    private readonly string _testRoot;
    private readonly Process _process;
    private readonly ConcurrentQueue<string> _output;

    private IisExpressTestServer(
        string testRoot, string hostRoot, string startupMarkerPath, int port, Process process, ConcurrentQueue<string> output)
    {
        _testRoot = testRoot;
        _hostRoot = hostRoot;
        _startupMarkerPath = startupMarkerPath;
        _process = process;
        _output = output;
        BaseAddress = new Uri($"http://localhost:{port}/");
    }

    public Uri BaseAddress { get; }

    public static async Task<IisExpressTestServer> StartAsync()
    {
        IisExpressInstallation installation = IisExpressTestEnvironment.GetInstallation();
        string testRoot = Path.Combine(TestHelpers.FunctionsTestDirectory, nameof(IisExpressStartupTimeoutTests), Guid.NewGuid().ToString("N"));
        string hostRoot = Path.Combine(testRoot, "host");
        string functionRoot = Path.Combine(testRoot, "function");
        string startupMarkerPath = Path.Combine(testRoot, "first-worker-started");
        Process process = null;

        try
        {
            string hostOutputPath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "WebJobs.Script.WebHost", TestHelpers.BuildConfig));
            string functionOutputPath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", TestWorkerProjectName, TestHelpers.BuildConfig));

            FileUtility.CopyDirectory(functionOutputPath, functionRoot);
            ConfigureHost(hostRoot, Path.Combine(hostOutputPath, "Microsoft.Azure.WebJobs.Script.WebHost.exe"));
            ConfigureWorker(functionRoot);

            int port = GetAvailablePort();
            string applicationHostConfigPath = await ConfigureIisExpressAsync(testRoot, hostRoot, installation, port);
            var output = new ConcurrentQueue<string>();
            process = StartIisExpress(
                installation.ExecutablePath, applicationHostConfigPath, functionRoot, Path.Combine(hostOutputPath, "workers"),
                startupMarkerPath, port, output);

            return new IisExpressTestServer(testRoot, hostRoot, startupMarkerPath, port, process, output);
        }
        catch
        {
            try
            {
                if (process is not null)
                {
                    await StopProcessAsync(process);
                }
            }
            finally
            {
                process?.Dispose();
                await DeleteDirectoryAsync(testRoot);
            }

            throw;
        }
    }

    public async Task<bool> WaitForStartupMarkerAsync(TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (File.Exists(_startupMarkerPath))
            {
                return true;
            }

            if (_process.HasExited)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return false;
    }

    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        if (_process.HasExited)
        {
            return true;
        }

        try
        {
            await _process.WaitForExitAsync().WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public Task StopAsync()
    {
        return StopProcessAsync(_process);
    }

    public string ReadAncmLog()
    {
        string logDirectory = Path.Combine(_hostRoot, "logs");
        if (!Directory.Exists(logDirectory))
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(logDirectory, "stdout*").SelectMany(File.ReadLines));
    }

    public string BuildDiagnostics(
        bool exitedWithoutTestCleanup, HttpStatusCode? statusCode, string response, Exception exception, string ancmLog)
    {
        string responseSummary = response is null ? "<none>" : response[..Math.Min(response.Length, 500)];
        string exceptionSummary = exception?.Message ?? "<none>";
        string[] signatureMessages =
        [
            "Starting JobHost",
            "Starting worker process failed",
            "Exceeded language worker restart retry count",
            "Host startup operation has been canceled",
            "Initialization cancellation requested by runtime",
            "Stopping JobHost",
            "Host started",
        ];
        string relevantAncmLog = string.Join(
            Environment.NewLine,
            (ancmLog ?? string.Empty)
                .Split(Environment.NewLine)
                .Where(line => signatureMessages.Any(message => line.Contains(message, StringComparison.Ordinal))));

        return new StringBuilder()
            .AppendLine($"IIS Express process ID: {_process.Id}")
            .AppendLine($"IIS Express exited without test cleanup: {exitedWithoutTestCleanup}")
            .AppendLine($"Last HTTP status: {statusCode?.ToString() ?? "<none>"}")
            .AppendLine($"Last response: {responseSummary}")
            .AppendLine($"Last request exception: {exceptionSummary}")
            .AppendLine("IIS Express output:")
            .AppendLine(string.Join(Environment.NewLine, _output.TakeLast(20)))
            .AppendLine("Relevant ANCM output:")
            .AppendLine(relevantAncmLog)
            .ToString();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync();
        }
        finally
        {
            _process.Dispose();
            await DeleteDirectoryAsync(_testRoot);
        }
    }

    private static Process StartIisExpress(
        string iisExpressPath, string applicationHostConfigPath, string functionRoot, string workersRoot, string startupMarkerPath,
        int port, ConcurrentQueue<string> output)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = iisExpressPath,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(iisExpressPath),
        };
        startInfo.ArgumentList.Add($"/config:{applicationHostConfigPath}");
        startInfo.ArgumentList.Add($"/site:{SiteName}");
        startInfo.ArgumentList.Add("/systray:false");
        startInfo.ArgumentList.Add("/trace:error");

        startInfo.Environment["AZURE_FUNCTIONS_ENVIRONMENT"] = "Development";
        startInfo.Environment["AzureWebJobsFeatureFlags"] = "EnableWorkerIndexing";
        startInfo.Environment["AzureWebJobsScriptRoot"] = functionRoot;
        startInfo.Environment["AzureWebJobsSecretStorageType"] = "files";
        startInfo.Environment["FUNCTIONS_TEST_WORKER_STARTUP_MARKER"] = startupMarkerPath;
        startInfo.Environment["FUNCTIONS_WORKER_RUNTIME"] = "dotnet-isolated";
        startInfo.Environment["FUNCTIONS_WORKER_RUNTIME_VERSION"] = "8.0";
        startInfo.Environment["WEBSITE_HOSTNAME"] = $"127.0.0.1:{port}";
        startInfo.Environment["WEBSITE_SITE_NAME"] = "iis-express-startup-timeout";
        startInfo.Environment["languageWorkers__workersDirectory"] = workersRoot;

        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start IIS Express.");
        process.OutputDataReceived += (_, args) => EnqueueOutput(output, args.Data);
        process.ErrorDataReceived += (_, args) => EnqueueOutput(output, args.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    private static void EnqueueOutput(ConcurrentQueue<string> output, string line)
    {
        if (line is not null)
        {
            output.Enqueue(line);
        }
    }

    private static void ConfigureHost(string hostRoot, string hostExecutablePath)
    {
        Directory.CreateDirectory(Path.Combine(hostRoot, "logs"));
        // Keep the IIS request-drain wait short for this test harness. Production uses the 30-second Generic Host default.
        string webConfig = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <system.webServer>
                <handlers>
                  <remove name="aspNetCore"/>
                  <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified"/>
                </handlers>
                <aspNetCore processPath="{SecurityElement.Escape(hostExecutablePath)}"
                            arguments="--shutdownTimeoutSeconds 5"
                            hostingModel="inprocess"
                            stdoutLogEnabled="true"
                            stdoutLogFile=".\logs\stdout"
                            startupTimeLimit="30" />
              </system.webServer>
            </configuration>
            """;

        File.WriteAllText(Path.Combine(hostRoot, "web.config"), webConfig);
    }

    private static async Task<string> ConfigureIisExpressAsync(
        string testRoot, string hostRoot, IisExpressInstallation installation, int port)
    {
        string applicationHostConfigPath = Path.Combine(testRoot, "applicationhost.config");
        File.Copy(installation.ConfigTemplatePath, applicationHostConfigPath);

        XDocument config = XDocument.Load(applicationHostConfigPath);
        XElement root = config.Root ?? throw new InvalidOperationException("The IIS Express configuration has no root element.");
        XElement sectionGroup = root.Element("configSections")
            ?.Elements("sectionGroup")
            .Single(p => string.Equals((string)p.Attribute("name"), "system.webServer", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The IIS Express system.webServer section group is missing.");

        XElement aspNetCoreSection = sectionGroup
            .Elements("section")
            .SingleOrDefault(p => string.Equals((string)p.Attribute("name"), "aspNetCore", StringComparison.Ordinal));
        if (aspNetCoreSection is null)
        {
            sectionGroup.Add(new XElement("section", new XAttribute("name", "aspNetCore"), new XAttribute("overrideModeDefault", "Allow")));
        }
        else
        {
            aspNetCoreSection.SetAttributeValue("overrideModeDefault", "Allow");
        }

        config.Save(applicationHostConfigPath);

        await RunAppCmdAsync(installation.AppCmdPath, applicationHostConfigPath,
            "unlock", "config", "/section:system.webServer/handlers");
        await RunAppCmdAsync(installation.AppCmdPath, applicationHostConfigPath,
            "install", "module", "/name:AspNetCoreModuleV2", $"/image:{installation.AncmPath}");
        await RunAppCmdAsync(installation.AppCmdPath, applicationHostConfigPath,
            "add", "apppool", $"/name:{ApplicationPoolName}", "/managedRuntimeVersion:", "/managedPipelineMode:Integrated");
        await RunAppCmdAsync(installation.AppCmdPath, applicationHostConfigPath,
            "add", "site", $"/name:{SiteName}", $"/bindings:http/*:{port}:localhost", $"/physicalPath:{hostRoot}");
        await RunAppCmdAsync(installation.AppCmdPath, applicationHostConfigPath,
            "set", "app", $"{SiteName}/", $"/applicationPool:{ApplicationPoolName}");

        return applicationHostConfigPath;
    }

    private static async Task RunAppCmdAsync(string appCmdPath, string applicationHostConfigPath, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = appCmdPath,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add($"/apphostconfig:{applicationHostConfigPath}");

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start IIS Express appcmd.");
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        string standardOutput = await standardOutputTask;
        string standardError = await standardErrorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"IIS Express appcmd failed with exit code {process.ExitCode}.{Environment.NewLine}" +
                $"Command: {string.Join(' ', arguments)}{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        }
    }

    private static void ConfigureWorker(string functionRoot)
    {
        string workerConfigPath = Path.Combine(functionRoot, "worker.config.json");
        JsonObject workerConfig = JsonNode.Parse(File.ReadAllText(workerConfigPath)).AsObject();
        workerConfig["processOptions"] = new JsonObject
        {
            ["processStartupTimeout"] = "00:00:08",
        };

        File.WriteAllText(workerConfigPath, workerConfig.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task DeleteDirectoryAsync(string path)
    {
        const int attempts = 10;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception exception) when (attempt < attempts && exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class IisExpressFactAttribute : FactAttribute
{
    public IisExpressFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "IIS Express tests require Windows.";
        }
        else if (!IisExpressTestEnvironment.IsRunRequired() && IisExpressTestEnvironment.TryGetInstallation() is null)
        {
            Skip = $"IIS Express prerequisites are unavailable. Set {IisExpressTestEnvironment.RunRequiredSettingName}=1 to require this test.";
        }
    }
}

internal static class IisExpressTestEnvironment
{
    public const string RunRequiredSettingName = "FUNCTIONS_RUN_IIS_EXPRESS_TESTS";

    private static readonly string[] IisExpressPaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "IIS Express", "iisexpress.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "IIS Express", "iisexpress.exe"),
    ];

    public static IisExpressInstallation GetInstallation()
    {
        return TryGetInstallation()
            ?? throw new InvalidOperationException(
                "IIS Express prerequisites are unavailable. Expected iisexpress.exe, appcmd.exe, ASP.NET Core Module V2, " +
                "and the PersonalWebServer applicationhost.config template under the IIS Express installation.");
    }

    public static IisExpressInstallation TryGetInstallation()
    {
        foreach (string executablePath in IisExpressPaths)
        {
            string installationRoot = Path.GetDirectoryName(executablePath);
            string appCmdPath = Path.Combine(installationRoot, "appcmd.exe");
            string ancmPath = Path.Combine(installationRoot, "Asp.Net Core Module", "V2", "aspnetcorev2.dll");
            string configTemplatePath = Path.Combine(
                installationRoot, "config", "templates", "PersonalWebServer", "applicationhost.config");
            if (File.Exists(executablePath) && File.Exists(appCmdPath) && File.Exists(ancmPath) && File.Exists(configTemplatePath))
            {
                return new IisExpressInstallation(executablePath, appCmdPath, ancmPath, configTemplatePath);
            }
        }

        return null;
    }

    public static bool IsRunRequired()
    {
        string value = Environment.GetEnvironmentVariable(RunRequiredSettingName);
        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record IisExpressInstallation(
    string ExecutablePath,
    string AppCmdPath,
    string AncmPath,
    string ConfigTemplatePath);
