// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
#if !NET46
#endif

namespace ExtensionsMetadataGenerator
{
    public class ExtensionsMetadataGenerator
    {
        private const string WebJobsStartupAttributeType = "Microsoft.Azure.WebJobs.Hosting.WebJobsStartupAttribute";
        private const string FunctionsStartupAttributeType = "Microsoft.Azure.Functions.Extensions.DependencyInjection.FunctionsStartupAttribute";

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
                            var currExtensionReferences = GenerateExtensionReferences(path, logger);
                            extensionReferences.AddRange(currExtensionReferences);

                            foreach (var foundRef in currExtensionReferences)
                            {
                                logger.LogMessage($"Found extension: {foundRef.TypeName}");
                            }
                        }
                        catch (BadImageFormatException)
                        {
                            // No need to log here as we can ignore these as extensions.
                        }
                        catch (Exception ex)
                        {
                            logger.LogError($"Could not evaluate '{Path.GetFileName(path)}' for extension metadata. Exception message: {ex}");
                        }
                    }
                }
            }

            string json = GenerateExtensionsJson(extensionReferences);
            File.WriteAllText(outputPath, json);
            logger.LogMessage($"'{outputPath}' successfully written.");
        }

        public static bool AssemblyShouldBeSkipped(string fileName) => fileName.StartsWith("System.", StringComparison.OrdinalIgnoreCase) || ExcludedAssemblies.Contains(fileName, StringComparer.OrdinalIgnoreCase);

        public static string GenerateExtensionsJson(IEnumerable<ExtensionReference> extensionReferences)
        {
            var referenceObjects = extensionReferences.Select(r => string.Format("{2}    {{ \"name\": \"{0}\", \"typeName\":\"{1}\"}}", r.Name, r.TypeName, Environment.NewLine));
            string json = string.Format("{{{1}  \"extensions\":[{0}{1}  ]{1}}}", string.Join(",", referenceObjects), Environment.NewLine);
            return json;
        }

        public static bool IsWebJobsStartupAttributeType(TypeReference attributeType, ConsoleLogger logger)
        {
            TypeReference currentAttributeType = attributeType;

            while (currentAttributeType != null)
            {
                if (string.Equals(currentAttributeType.FullName, WebJobsStartupAttributeType, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                try
                {
                    currentAttributeType = currentAttributeType.Resolve()?.BaseType;
                }
                catch (FileNotFoundException ex)
                {
                    // Don't log this as an error. This will almost always happen due to some publishing artifacts (i.e. Razor) existing
                    // in the functions bin folder without all of their dependencies present. These will almost never have Functions extensions,
                    // so we don't want to write out errors every time there is a build. This message can be seen with detailed logging enabled.
                    string attributeTypeName = GetReflectionFullName(attributeType);
                    string fileName = Path.GetFileName(attributeType.Module.FileName);
                    logger.LogMessage($"Could not determine whether the attribute type '{attributeTypeName}' used in the assembly '{fileName}' derives from '{WebJobsStartupAttributeType}' because the assembly defining its base type could not be found. Exception message: {ex.Message}");
                    return false;
                }
            }

            return false;
        }

        public static IEnumerable<ExtensionReference> GenerateExtensionReferences(string fileName, ConsoleLogger logger)
        {
            var resolver = new FunctionsAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(fileName));

            ReaderParameters readerParams = new ReaderParameters { AssemblyResolver = resolver };

            AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(fileName, readerParams);

            var startupAttributes = assembly.Modules.SelectMany(p => p.GetCustomAttributes())
                .Where(a => IsWebJobsStartupAttributeType(a.AttributeType, logger));

            List<ExtensionReference> extensionReferences = new List<ExtensionReference>();
            foreach (var attribute in startupAttributes)
            {
                var typeProperty = attribute.ConstructorArguments.ElementAtOrDefault(0);
                var nameProperty = attribute.ConstructorArguments.ElementAtOrDefault(1);

                TypeDefinition typeDef = (TypeDefinition)typeProperty.Value;
                string assemblyQualifiedName = Assembly.CreateQualifiedName(typeDef.Module.Assembly.FullName, GetReflectionFullName(typeDef));

                string name;

                // Because we're now using static analysis we can't rely on the constructor running so have to get the name ourselves.
                if (string.Equals(attribute.AttributeType.FullName, FunctionsStartupAttributeType, StringComparison.OrdinalIgnoreCase))
                {
                    // FunctionsStartup always uses the type name as the name.
                    name = typeDef.Name;
                }
                else
                {
                    // WebJobsStartup does some trimming.
                    name = GetName((string)nameProperty.Value, typeDef);
                }

                var extensionReference = new ExtensionReference
                {
                    Name = name,
                    TypeName = assemblyQualifiedName
                };

                extensionReferences.Add(extensionReference);
            }

            return extensionReferences;
        }

        // Copying the WebJobsStartup constructor logic from:
        // https://github.com/Azure/azure-webjobs-sdk/blob/e5417775bcb8c8d3d53698932ca8e4e265eac66d/src/Microsoft.Azure.WebJobs.Host/Hosting/WebJobsStartupAttribute.cs#L33-L47.
        private static string GetName(string name, TypeDefinition startupTypeDef)
        {
            if (string.IsNullOrEmpty(name))
            {
                // for a startup class named 'CustomConfigWebJobsStartup' or 'CustomConfigStartup',
                // default to a name 'CustomConfig'
                name = startupTypeDef.Name;
                int idx = name.IndexOf("WebJobsStartup");
                if (idx < 0)
                {
                    idx = name.IndexOf("Startup");
                }
                if (idx > 0)
                {
                    name = name.Substring(0, idx);
                }
            }

            return name;
        }

        public static string GetReflectionFullName(TypeReference typeRef)
        {
            return typeRef.FullName.Replace("/", "+");
        }

        public class FunctionsAssemblyResolver : DefaultAssemblyResolver
        {
            private static readonly HashSet<string> _trustedPlatformAssemblies = GetTrustedPlatformAssemblies();

            private static HashSet<string> GetTrustedPlatformAssemblies()
            {
                var data = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
                var tpaArr = data.ToString().Split(Path.PathSeparator);
                var tpaHash = new HashSet<string>(tpaArr.Length, StringComparer.OrdinalIgnoreCase);

                foreach (var tpa in tpaArr)
                {
                    tpaHash.Add(tpa);
                }

                return tpaHash;
            }

            public override AssemblyDefinition Resolve(AssemblyNameReference name)
            {
                var assemblyDef = base.Resolve(name);

                // As soon as we get to a TPA we can stop. This helps prevent the odd circular reference
                // with type forwarders as well.
                if (_trustedPlatformAssemblies.Contains(assemblyDef.MainModule.FileName))
                {
                    return null;
                }

                return assemblyDef;
            }
        }
    }
}
