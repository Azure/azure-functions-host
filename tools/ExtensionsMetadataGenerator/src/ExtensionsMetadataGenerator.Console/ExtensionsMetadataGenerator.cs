// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
#if !NET46
using System.Runtime.Loader;
#endif

namespace ExtensionsMetadataGenerator
{
    public class ExtensionsMetadataGenerator
    {
        private const string WebJobsStartupAttributeType = "Microsoft.Azure.WebJobs.Hosting.WebJobsStartupAttribute";

        // These assemblies are always loaded by the functions runtime and should not be listed in extensions.json
        private static readonly string[] ExcludedAssemblies = new[] { "Microsoft.Azure.WebJobs.Extensions.dll", "Microsoft.Azure.WebJobs.Extensions.Http.dll" };

        public static void Generate(string sourcePath, string outputPath, Action<string> logger)
        {
            if (!Directory.Exists(sourcePath))
            {
                throw new DirectoryNotFoundException($"The path `{sourcePath}` does not exist. Unable to generate Azure Functions extensions metadata file.");
            }

            var extensionReferences = new List<ExtensionReference>();

            var targetAssemblies = Directory.EnumerateFiles(sourcePath, "*.dll")
                .Where(f => !AssemblyShouldBeSkipped(Path.GetFileName(f)));

            foreach (var path in targetAssemblies)
            {
                try
                {
                    Assembly assembly = Assembly.LoadFrom(path);
                    var currExtensionReferences = GenerateExtensionReferences(assembly);
                    extensionReferences.AddRange(currExtensionReferences);
                }
                catch (Exception exc)
                {
                    logger(exc.Message ?? $"Error processing {path}");
                }
            }

            string json = GenerateExtensionsJson(extensionReferences);
            File.WriteAllText(outputPath, json);
        }

        public static bool AssemblyShouldBeSkipped(string fileName) => fileName.StartsWith("System", StringComparison.OrdinalIgnoreCase) || ExcludedAssemblies.Contains(fileName, StringComparer.OrdinalIgnoreCase);

        public static string GenerateExtensionsJson(IEnumerable<ExtensionReference> extensionReferences)
        {
            var referenceObjects = extensionReferences.Select(r => string.Format("{2}    {{ \"name\": \"{0}\", \"typeName\":\"{1}\"}}", r.Name, r.TypeName, Environment.NewLine));
            string json = string.Format("{{{1}  \"extensions\":[{0}{1}  ]{1}}}", string.Join(",", referenceObjects), Environment.NewLine);
            return json;
        }

        public static IEnumerable<ExtensionReference> GenerateExtensionReferences(Assembly assembly)
        {
            var startupAttributes = assembly.GetCustomAttributes().Where(a => string.Equals(a.GetType().FullName, WebJobsStartupAttributeType, StringComparison.OrdinalIgnoreCase));

            List<ExtensionReference> extensionReferences = new List<ExtensionReference>();
            foreach (var attribute in startupAttributes)
            {
                var nameProperty = attribute.GetType().GetProperty("Name");
                var typeProperty = attribute.GetType().GetProperty("WebJobsStartupType");

                var extensionReference = new ExtensionReference
                {
                    Name = (string)nameProperty.GetValue(attribute),
                    TypeName = ((Type)typeProperty.GetValue(attribute)).AssemblyQualifiedName
                };

                extensionReferences.Add(extensionReference);
            }

            return extensionReferences;
        }
    }
}
