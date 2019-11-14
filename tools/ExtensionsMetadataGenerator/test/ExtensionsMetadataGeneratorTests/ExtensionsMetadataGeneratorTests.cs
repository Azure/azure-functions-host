// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using ExtensionsMetadataGenerator;
using ExtensionsMetadataGenerator.Console;
using Microsoft.Azure.WebJobs.Hosting;
using Mono.Cecil;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ExtensionsMetadataGeneratorTests
{
    public class ExtensionsMetadataGeneratorTests
    {
        private ConsoleLogger _logger = new ConsoleLogger();

        [Fact]
        public void Generator_DifferentTargetFrameworks()
        {
            // The TestProject_Core21.dll, TestProject_Core22.dll, and TestProject_Razor.dll will be in the output directory.
            string sourcePath = Path.GetDirectoryName(GetType().Assembly.Location);
            string outputFile = Path.Combine(Path.GetTempPath(), "Functions_ExtensionsMetadataGeneratorTests", $"{DateTime.UtcNow.Ticks}.json");

            string outputDir = Path.GetDirectoryName(outputFile);
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }
            Directory.CreateDirectory(outputDir);

            var log = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(log);

            try
            {
                Program.Main(new[] { sourcePath, outputFile });

                string extensionsJson = File.ReadAllText(outputFile);
                JObject json = JObject.Parse(extensionsJson);

                // We expect to see the Startups from this assembly (running on Core 2.0),
                // plus the two from the 2.1 and 2.2 test projects.
                JToken extensions = json["extensions"];
                int startups = extensions.Count();
                Assert.Equal(5, startups);

                Assert.Single(extensions, e => e["name"].ToString() == "Foo" && e["typeName"].ToString().StartsWith("ExtensionsMetadataGeneratorTests.FooWebJobsStartup, ExtensionsMetadataGeneratorTests"));
                Assert.Single(extensions, e => e["name"].ToString() == "BarExtension" && e["typeName"].ToString().StartsWith("ExtensionsMetadataGeneratorTests.BarWebJobsStartup, ExtensionsMetadataGeneratorTests"));
                Assert.Single(extensions, e => e["name"].ToString() == "Startup" && e["typeName"].ToString().StartsWith("TestProject_Core21.Startup"));
                Assert.Single(extensions, e => e["name"].ToString() == "Startup" && e["typeName"].ToString().StartsWith("TestProject_Core22.Startup"));
                Assert.Single(extensions, e => e["name"].ToString() == "Startup" && e["typeName"].ToString().StartsWith("TestProject_Razor.Startup"));

                // We log a message here, but have successfully found the TestProject_Razor.Startup.
                Assert.Contains("Could not determine whether the attribute type 'Microsoft.AspNetCore.Razor.Hosting.RazorExtensionAssemblyNameAttribute' used in the assembly 'TestProject_Razor.dll' derives from 'Microsoft.Azure.WebJobs.Hosting.WebJobsStartupAttribute'", log.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void GenerateExtensionReferences_Succeeds()
        {
            var fileName = GetType().Assembly.Location;
            var references = ExtensionsMetadataGenerator.ExtensionsMetadataGenerator.GenerateExtensionReferences(fileName, _logger).ToArray();
            Assert.Equal(2, references.Length);

            Assert.Equal("Foo", references[0].Name);
            Assert.Equal(typeof(FooWebJobsStartup).AssemblyQualifiedName, references[0].TypeName);

            Assert.Equal("BarExtension", references[1].Name);
            Assert.Equal(typeof(BarWebJobsStartup).AssemblyQualifiedName, references[1].TypeName);
        }

        [Fact]
        public void GenerateExtensionsJson_Succeeds()
        {
            var assembly = GetType().Assembly.Location;
            var references = ExtensionsMetadataGenerator.ExtensionsMetadataGenerator.GenerateExtensionReferences(assembly, _logger).ToArray();
            string json = ExtensionsMetadataGenerator.ExtensionsMetadataGenerator.GenerateExtensionsJson(references);

            var root = JObject.Parse(json);
            var extensions = root["extensions"];

            Assert.Equal("Foo", extensions[0]["name"]);
            Assert.Equal(typeof(FooWebJobsStartup).AssemblyQualifiedName, extensions[0]["typeName"]);

            Assert.Equal("BarExtension", extensions[1]["name"]);
            Assert.Equal(typeof(BarWebJobsStartup).AssemblyQualifiedName, extensions[1]["typeName"]);
        }

        [Theory]
        [InlineData(typeof(WebJobsStartupAttribute), true)]
        [InlineData(typeof(TestStartupAttribute), true)]
        [InlineData(typeof(CLSCompliantAttribute), false)]
        [InlineData(typeof(Attribute), false)]
        public void IsWebJobsStartupAttributeType_CorrectlyIdentifiesAttributes(Type attributeType, bool isWebJobsType)
        {
            ModuleDefinition module = ModuleDefinition.ReadModule(attributeType.Assembly.Location);
            TypeReference typeRef = module.GetTypes().Single(p => p.FullName == attributeType.FullName);

            bool result = ExtensionsMetadataGenerator.ExtensionsMetadataGenerator.IsWebJobsStartupAttributeType(typeRef, _logger);

            Assert.Equal(isWebJobsType, result);
        }

        [Theory]
        [InlineData("System.Linq.dll", true)]
        [InlineData("Microsoft.Azure.WebJobs.Extensions.dll", true)]
        [InlineData("Microsoft.Azure.WebJobs.Extensions.Http.dll", true)]
        [InlineData("microsoft.azure.webjobs.extensions.http.dll", true)]
        [InlineData("Microsoft.Azure.EventGrid.dll", false)]
        [InlineData("MyCoolExtension.dll", false)]
        public void SomeAssembliesAreSkipped(string assemblyFileName, bool shouldSkip)
        {
            Assert.Equal(shouldSkip, ExtensionsMetadataGenerator.ExtensionsMetadataGenerator.AssemblyShouldBeSkipped(assemblyFileName));
        }
    }
}
