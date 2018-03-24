// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator;
#if !NET46
using System.Runtime.Loader;
#endif

namespace ExtensionsMetadataGenerator
{
    public class ExtensionsMetadataGenerator
    {
        public static void Generate(string sourcePath, string outputPath, Action<string> logger)
        {
            if (!Directory.Exists(sourcePath))
            {
                throw new DirectoryNotFoundException($"The path `{sourcePath}` does not exist. Unable to generate Azure Functions extensions metadata file.");
            }

            var extensionReferences = new List<ExtensionReference>();

            var targetAssemblies = Directory.EnumerateFiles(sourcePath, "*.dll")
                .Where(f => !Path.GetFileName(f).StartsWith("System", StringComparison.OrdinalIgnoreCase));

            foreach (var path in targetAssemblies)
            {
                try
                {
                    Assembly assembly = Assembly.LoadFrom(path);

                    var extensions = assembly.GetExportedTypes()
                        .Where(t => t.IsExtensionType())
                        .Select(t => new ExtensionReference
                        {
                            Name = t.Name,
                            TypeName = t.AssemblyQualifiedName
                        });

                    extensionReferences.AddRange(extensions);
                }
                catch (Exception exc)
                {
                    logger(exc.Message ?? $"Errot processing {path}");
                }
            }

            var referenceObjects = extensionReferences.Select(r => string.Format("{2}    {{ \"name\": \"{0}\", \"typeName\":\"{1}\"}}", r.Name, r.TypeName, Environment.NewLine));
            string metadataContents = string.Format("{{{1}  \"extensions\":[{0}{1}  ]{1}}}", string.Join(",", referenceObjects), Environment.NewLine);
            File.WriteAllText(outputPath, metadataContents);
        }
    }
}
