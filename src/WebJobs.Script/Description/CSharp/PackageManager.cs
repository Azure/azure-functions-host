using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Provides NuGet package management functionality.
    /// </summary>
    internal sealed class PackageManager
    {
        private readonly FunctionMetadata _functionMetadata;
        private readonly TraceWriter _traceWriter;

        public PackageManager(FunctionMetadata metadata, TraceWriter traceWriter)
        {
            _functionMetadata = metadata;
            _traceWriter = traceWriter;
        }

        public Task RestorePackagesAsync()
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                string projectPath = Path.Combine(Path.GetDirectoryName(_functionMetadata.Source), CSharpConstants.ProjectFileName);

                var startInfo = new ProcessStartInfo
                {
                    // TODO: Hardcoding this for some tests...
                    FileName = @"D:\home\SiteExtensions\Kudu\bin\Scripts\NuGet.exe",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    ErrorDialog = false,
                    Arguments = FormattableString.Invariant($"restore \"{projectPath}\"")
                };

                PopulateEnvironment(startInfo);

                var process = new Process { StartInfo = startInfo };
                process.ErrorDataReceived += ProcessDataReceived;
                process.OutputDataReceived += ProcessDataReceived;
                process.EnableRaisingEvents = true;

                process.Exited += (s, e) =>
                {
                    tcs.SetResult(process.ExitCode == 0);
                    process.Close();
                };

                _traceWriter.Verbose("Starting NuGet restore");

                process.Start();
                
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
            }
            catch (Exception exc)
            {
                tcs.SetException(exc);
            }

            return tcs.Task;
        }

        private static void PopulateEnvironment(ProcessStartInfo startInfo)
        {
            var environment = Environment.GetEnvironmentVariables();

            foreach (DictionaryEntry item in environment)
            {
                if (!startInfo.EnvironmentVariables.ContainsKey(item.Key.ToString()))
                {
                    startInfo.EnvironmentVariables.Add(item.Key.ToString(), item.Value.ToString());
                }
            }
        }

        private void ProcessDataReceived(object sender, DataReceivedEventArgs e)
        {
            _traceWriter.Verbose(e.Data ?? string.Empty);
        }
    }
}
