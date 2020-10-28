// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
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
        [Required]
        public string OutputPath { get; set; }

        [Required]
        public ITaskItem[] IgnoreFiles { get; set; }

        public override bool Execute()
        {
            HashSet<string> ignoreFilesSet = new HashSet<string>();
            foreach (ITaskItem item in IgnoreFiles)
            {
                ignoreFilesSet.Add(item.ItemSpec);
            }

            Assembly assembly = typeof(RemoveRuntimeDependencies).Assembly;
            using (Stream resource = assembly.GetManifestResourceStream(assembly.GetName().Name + ".runtimeassemblies.txt"))
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