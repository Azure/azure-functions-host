// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Provides NuGet package management functionality.
    /// </summary>
    internal sealed class PackageManager
    {
        private const string NugetPathEnvironmentKey = "AzureWebJobs_NuGetPath";
        private const string NuGetFileName = "nuget.exe";
        
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
                    FileName = ResolveNuGetPath(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    ErrorDialog = false,
                    Arguments = "restore \"" + projectPath + "\""
                };

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

        public static string ResolveNuGetPath()
        {
            // Check if we have the path in the well known environment variable
            string path = Environment.GetEnvironmentVariable(NugetPathEnvironmentKey);

            //// If we don't have the path, try to get a fully qualified path to Kudu's NuGet copy.
            if (string.IsNullOrEmpty(path))
            {
                // Get the latest Kudu extension path
                string kuduPath = Directory.GetDirectories(Environment.ExpandEnvironmentVariables("%programfiles(x86)%\\siteextensions\\kudu"))
                    .OrderByDescending(d => d).FirstOrDefault();

                if (!string.IsNullOrEmpty(kuduPath))
                {
                    path = Path.Combine(kuduPath, "\\bin\\scripts", NuGetFileName);
                }                
            }

            // Return the resolved value or expect NuGet.exe to be present in the path.
            return path ?? NuGetFileName;
        }

        private void ProcessDataReceived(object sender, DataReceivedEventArgs e)
        {
            _traceWriter.Verbose(e.Data ?? string.Empty);
        }
    }
}
