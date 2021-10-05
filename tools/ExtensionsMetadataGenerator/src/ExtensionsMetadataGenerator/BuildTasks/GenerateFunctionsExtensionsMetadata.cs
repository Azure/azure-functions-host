// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace ExtensionsMetadataGenerator.BuildTasks
{
#if NET46
    public class GenerateFunctionsExtensionsMetadata : AppDomainIsolatedTask
#else
    public class GenerateFunctionsExtensionsMetadata : Task
#endif
    {
        [Required]
        public string SourcePath { get; set; }

        [Required]
        public string OutputPath { get; set; }

        public override bool Execute()
        {
            string outputPath = Path.Combine(OutputPath, "extensions.json");

            if (SourcePath.EndsWith("\\"))
            {
                SourcePath = Path.GetDirectoryName(SourcePath);
            }

            Assembly taskAssembly = typeof(GenerateFunctionsExtensionsMetadata).Assembly;

            var info = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = Path.Combine(Path.GetDirectoryName(taskAssembly.Location), "..", "netstandard2.0", "generator"),
                FileName = DotNetMuxer.MuxerPathOrDefault(),
                Arguments = $"Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator.Console.dll \"{SourcePath}\" \"{outputPath}\""
            };

            Log.LogMessage(MessageImportance.Low, $"Extensions generator working directory: '{info.WorkingDirectory}'");
            Log.LogMessage(MessageImportance.Low, $"Extensions generator path: '{info.FileName}'");
            Log.LogCommandLine(MessageImportance.Low, info.Arguments);

            using (var process = new Process { StartInfo = info })
            {
                process.EnableRaisingEvents = true;

                StringBuilder errorString = new StringBuilder();
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        Log.LogWarning(e.Data);
                        errorString.Append(e.Data);
                    }
                };

                StringBuilder outputString = new StringBuilder();
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        // These debug logs will only appear in builds with detailed or higher verbosity.
                        Log.LogMessage(MessageImportance.Low, e.Data);
                        outputString.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    // Dump any debug output if there is an error. This may have been hidden due to the msbuild verbosity level.
                    Log.LogMessage(MessageImportance.High, "Debug output from extension.json generator:");
                    Log.LogMessage(MessageImportance.High, outputString.ToString());
                    Log.LogError($"Metadata generation failed. Exit code: '{process.ExitCode}' Error: '{errorString}'");
                    return false;
                }

                return true;
            }
        }
    }
}
