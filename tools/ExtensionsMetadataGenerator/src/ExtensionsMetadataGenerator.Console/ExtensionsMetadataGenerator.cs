// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
#if !NET46
#endif

namespace ExtensionsMetadataGenerator
{
    public class ExtensionsMetadataGenerator
    {
        private const string WebJobsStartupAttributeType = "Microsoft.Azure.WebJobs.Hosting.WebJobsStartupAttribute";

        // These assemblies are always loaded by the functions runtime and should not be listed in extensions.json
        private static readonly string[] ExcludedAssemblies = new[] { "Microsoft.Azure.WebJobs.Extensions.dll", "Microsoft.Azure.WebJobs.Extensions.Http.dll" };

        public static void Generate(string sourcePath, string outputPath, ConsoleLogger logger)
        {
            if (!Directory.Exists(sourcePath))
            {
                throw new DirectoryNotFoundException($"The path `{sourcePath}` does not exist. Unable to generate Azure Functions extensions metadata file.");
            }

            var extensionReferences = new List<ExtensionReference>();

            var targetAssemblies = Directory.EnumerateFiles(sourcePath, "*.dll")
                .Where(f => !AssemblyShouldBeSkipped(Path.GetFileName(f)))
                .ToArray();

            logger.LogMessage($"Found {targetAssemblies.Length} assemblies to evaluate in '{sourcePath}':");

            foreach (var path in targetAssemblies)
            {
                using (logger.Indent())
                {
                    logger.LogMessage($"{Path.GetFileName(path)}");

                    using (logger.Indent())
                    {
                        try
                        {
                            Assembly assembly = Assembly.LoadFrom(path);
                            var currExtensionReferences = GenerateExtensionReferences(assembly);
                            extensionReferences.AddRange(currExtensionReferences);

                            foreach (var foundRef in currExtensionReferences)
                            {
                                logger.LogMessage($"Found extension: {foundRef.TypeName}");
                            }
                        }
                        catch (Exception exc)
                        {
                            logger.LogError($"Could not evaluate '{Path.GetFileName(path)}' for extension metadata. If this assembly contains a Functions extension, ensure that all dependent assemblies exist in '{sourcePath}'. If this assembly does not contain any Functions extensions, this message can be ignored. Exception message: {exc.Message}");
                        }
                    }
                }
            }

            string json = GenerateExtensionsJson(extensionReferences);
            File.WriteAllText(outputPath, json);
            logger.LogMessage($"'{outputPath}' successfully written.");
        }

        public static bool AssemblyShouldBeSkipped(string fileName) => fileName.StartsWith("System", StringComparison.OrdinalIgnoreCase) || ExcludedAssemblies.Contains(fileName, StringComparer.OrdinalIgnoreCase);

        public static string GenerateExtensionsJson(IEnumerable<ExtensionReference> extensionReferences)
        {
            var referenceObjects = extensionReferences.Select(r => string.Format("{2}    {{ \"name\": \"{0}\", \"typeName\":\"{1}\"}}", r.Name, r.TypeName, Environment.NewLine));
            string json = string.Format("{{{1}  \"extensions\":[{0}{1}  ]{1}}}", string.Join(",", referenceObjects), Environment.NewLine);
            return json;
        }

        public static bool IsWebJobsStartupAttributeType(Type attributeType)
        {
            Type currentType = attributeType;

            while (currentType != null)
            {
                if (string.Equals(currentType.FullName, WebJobsStartupAttributeType, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                currentType = currentType.BaseType;
            }

            return false;
        }

        public static IEnumerable<ExtensionReference> GenerateExtensionReferences(Assembly assembly)
        {
            var startupAttributes = assembly.GetCustomAttributes()
                .Where(a => IsWebJobsStartupAttributeType(a.GetType()));

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
