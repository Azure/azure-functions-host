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
        private static readonly IDictionary<string, string> _runtimeToAssemblyFileMap = new Dictionary<string, string>()
        {
            { "net6.0", ".runtimeAssemblies-net6.txt" },
            { "net8.0", ".runtimeAssemblies-net8.txt" }
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
                Log.LogError($"The TargetFramework '{TargetFramework}' is not supported in this project.");
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