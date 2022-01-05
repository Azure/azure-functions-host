using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    /// <summary>
    /// Some tests, specifically those using AssemblyLoadContext, cannot be run inside of XUnit. This class
    /// launches a separate process which can then be hit with the provided HttpClient.
    /// </summary>
    public class HostProcessLauncher : IDisposable
    {
        private const string TestPathTemplate = "..\\..\\..\\..\\..\\test\\CSharpPrecompiledTestProjects\\{0}\\bin\\Debug\\{1}";
        private const int _port = 3479;

        private readonly string _testPath;
        private readonly IDictionary<string, string> _envVars;
        private readonly Process _process = new Process();
        private readonly IList<string> _outputLogs = new List<string>();
        private readonly IList<string> _errorLogs = new List<string>();

        private Lazy<HttpClient> _lazyClient = new Lazy<HttpClient>(() =>
        {
            var client = new HttpClient();
            client.BaseAddress = new Uri($"http://localhost:{_port}");
            return client;
        });

        public HostProcessLauncher(string testProjectName, IDictionary<string, string> envVars = null,
            bool usePublishPath = false, string targetFramework = "netcoreapp3.1")
        {
            _testPath = Path.GetFullPath(string.Format(TestPathTemplate, testProjectName, targetFramework));

            if (usePublishPath)
            {
                _testPath = Path.Combine(_testPath, "publish");
            }

            _envVars = envVars ?? new Dictionary<string, string>();
        }

        internal HttpClient HttpClient => _lazyClient.Value;

        internal IEnumerable<string> OutputLogs => _outputLogs;

        internal IEnumerable<string> ErrorLogs => _errorLogs;

        public async Task StartHostAsync(ITestOutputHelper outputHelper = null)
        {
            // WebHost folder name will match this one.
            var dirPath = Path.GetDirectoryName(typeof(HostProcessLauncher).Assembly.Location);
            var dirName = new DirectoryInfo(dirPath).Name;

            string workingDir = Path.Combine("..", "..", "..", "..", "..", "src", "WebJobs.Script.WebHost", "bin", "Debug", $"{dirName}");
            workingDir = Path.GetFullPath(workingDir);
            string filePath = Path.Combine(workingDir, "Microsoft.Azure.WebJobs.Script.WebHost.exe");

            outputHelper?.WriteLine($"Test: {_testPath}");
            outputHelper?.WriteLine($"  Test host.json exists: {File.Exists(Path.Join(_testPath, "host.json"))}");
            outputHelper?.WriteLine($"  Exe:                   {filePath}");
            outputHelper?.WriteLine($"  Exe exists:            {File.Exists(filePath)}");
            outputHelper?.WriteLine($"  Port:                  {_port}");

            _process.StartInfo = new ProcessStartInfo
            {
                FileName = filePath,
                Arguments = $"--urls=http://localhost:{_port}",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                UseShellExecute = false,
                ErrorDialog = false,
                WorkingDirectory = workingDir
            };

            _process.StartInfo.Environment.Add("AzureWebJobsScriptRoot", _testPath);
            _process.StartInfo.Environment.Add("AZURE_FUNCTIONS_ENVIRONMENT", "Development");

            foreach (var envVar in _envVars)
            {
                _process.StartInfo.Environment.Add(envVar.Key, envVar.Value);
            }

            _process.EnableRaisingEvents = true;
            _process.OutputDataReceived += Process_OutputDataReceived;
            _process.ErrorDataReceived += Process_ErrorDataReceived;

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            await TestHelpers.Await(() => IsHostRunningAsync(HttpClient));
        }

        private static async Task<bool> IsHostRunningAsync(HttpClient client)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, string.Empty))
            {
                try
                {
                    using (HttpResponseMessage response = await client.SendAsync(request))
                    {
                        return response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.OK;
                    }
                }
                catch
                {
                    // The process may not be ready yet; Return false and let it retry.
                    return false;
                }
            }
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                _outputLogs.Add($"[{DateTime.UtcNow:O}] {e.Data}");
            }
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                _outputLogs.Add($"[{DateTime.UtcNow:O}] {e.Data}");
            }
        }

        public void Dispose()
        {
            _process?.Kill();
            _process?.WaitForExit();
        }
    }
}
