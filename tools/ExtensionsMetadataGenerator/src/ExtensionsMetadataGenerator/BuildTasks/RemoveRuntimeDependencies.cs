// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator.BuildTasks
{
#if NET46
    public class RemoveRuntimeDependencies : AppDomainIsolatedTask
#else
    public class RemoveRuntimeDependencies : Task
#endif
    {
        private const string Net6File = ".runtimeAssemblies-net6.txt";
        private const string Net8File = ".runtimeAssemblies-net8.txt";

        // everything up until net6 maps to the net6 assembly list
        // net8 only maps to the net8 assembly list
        // anything else (such as future versions) are unsupported
        private static readonly IDictionary<string, string> _runtimeToAssemblyFileMap = new Dictionary<string, string>()
        {
            { "netstandard2.0", Net6File },
            { "netstandard2.1", Net6File },
            { "netcoreapp2.1", Net6File },
            { "netcoreapp2.2", Net6File },
            { "netcoreapp3.0", Net6File },
            { "netcoreapp2.0", Net6File },
            { "netcoreapp3.1", Net6File },
            { "net5.0", Net6File },
            { "net6.0", Net6File },

            { "net8.0", Net8File }
        };

        [Required]
        public string OutputPath { get; set; }

        [Required]
        public ITaskItem[] IgnoreFiles { get; set; }

        [Required]
        public string TargetFramework { get; set; }

        public override bool Execute()
        {
            HashSet<string> ignoreFilesSet = new HashSet<string>();
            foreach (ITaskItem item in IgnoreFiles)
            {
                ignoreFilesSet.Add(item.ItemSpec);
            }

            if (!_runtimeToAssemblyFileMap.TryGetValue(TargetFramework, out var assemblyListFileName))
            {
                Log.LogError($"The TargetFramework '{TargetFramework}' is not supported in this project. Supported frameworks are: {string.Join(", ", _runtimeToAssemblyFileMap.Keys)}.");
                return false;
            }

            Assembly assembly = typeof(RemoveRuntimeDependencies).Assembly;
            using (Stream resource = assembly.GetManifestResourceStream(assembly.GetName().Name + assemblyListFileName))
            using (var reader = new StreamReader(resource))
            {
                string assemblyName = reader.ReadLine();
                while (!string.IsNullOrEmpty(assemblyName))
                {
                    string fileName = Path.Combine(OutputPath, assemblyName);

                    if (File.Exists(fileName) && !ignoreFilesSet.Contains(assemblyName))
                    {
                        File.Delete(fileName);
                    }

                    assemblyName = reader.ReadLine();
                }
            }

            return true;
        }
    }
}