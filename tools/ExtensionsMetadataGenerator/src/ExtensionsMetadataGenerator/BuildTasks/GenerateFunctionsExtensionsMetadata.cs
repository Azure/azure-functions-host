// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

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
                CreateNoWindow = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = Path.Combine(Path.GetDirectoryName(taskAssembly.Location), "..", "netstandard2.0", "generator"),
                FileName = DotNetMuxer.MuxerPathOrDefault(),
                Arguments = $"Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator.Console.dll \"{SourcePath}\" \"{outputPath}\""
            };

            var process = new Process { StartInfo = info };
            process.EnableRaisingEvents = true;
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) Log.LogWarning(e.Data); };
            
            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Log.LogError("Metadata generation failed.");

                return false;
            }

            process.Close();

            return true;
        }
    }
}
