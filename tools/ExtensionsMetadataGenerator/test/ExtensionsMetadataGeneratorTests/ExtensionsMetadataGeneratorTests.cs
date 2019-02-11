// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using ExtensionsMetadataGenerator.Console;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ExtensionsMetadataGeneratorTests
{
    public class ExtensionsMetadataGeneratorTests
    {
        [Fact]
        public void Generator_DifferentTargetFrameworks()
        {
            // The TestProject_Core22.dll will be in the output directory.
            string sourcePath = Path.GetDirectoryName(GetType().Assembly.Location);
            string outputFile = Path.Combine(Path.GetTempPath(), "Functions_ExtensionsMetadataGeneratorTests", $"{DateTime.UtcNow.Ticks}.json");

            string outputDir = Path.GetDirectoryName(outputFile);
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }
            Directory.CreateDirectory(outputDir);

            Program.Main(new[] { sourcePath, outputFile });

            string extensionsJson = File.ReadAllText(outputFile);
            JObject json = JObject.Parse(extensionsJson);

            // We expect to see the Startups from this assembly (running on Core 2.0),
            // plus the two from the 2.1 and 2.2 test projects.
            JToken extensions = json["extensions"];
            int startups = extensions.Count();
            Assert.Equal(4, startups);

            Assert.Single(extensions, e => e["name"].ToString() == "Foo" && e["typeName"].ToString().StartsWith("ExtensionsMetadataGeneratorTests.FooWebJobsStartup, ExtensionsMetadataGeneratorTests"));
            Assert.Single(extensions, e => e["name"].ToString() == "BarExtension" && e["typeName"].ToString().StartsWith("ExtensionsMetadataGeneratorTests.BarWebJobsStartup, ExtensionsMetadataGeneratorTests"));
            Assert.Single(extensions, e => e["name"].ToString() == "Startup" && e["typeName"].ToString().StartsWith("TestProject_Core21.Startup"));
            Assert.Single(extensions, e => e["name"].ToString() == "Startup" && e["typeName"].ToString().StartsWith("TestProject_Core22.Startup"));
        }

        [Fact]
        public void GenerateExtensionReferences_Succeeds()
        {
            var assembly = GetType().Assembly;
            var references = ExtensionsMetadataGenerator.ExtensionsMetadataGenerator.GenerateExtensionReferences(assembly).ToArray();
            Assert.Equal(2, references.Length);

            Assert.Equal("Foo", references[0].Name);
            Assert.Equal(typeof(FooWebJobsStartup).AssemblyQualifiedName, references[0].TypeName);

            Assert.Equal("BarExtension", references[1].Name);
            Assert.Equal(typeof(BarWebJobsStartup).AssemblyQualifiedName, references[1].TypeName);
        }

        [Fact]
        public void GenerateExtensionsJson_Succeeds()
        {
            var assembly = GetType().Assembly;
            var references = ExtensionsMetadataGenerator.ExtensionsMetadataGenerator.GenerateExtensionReferences(assembly).ToArray();
            string json = ExtensionsMetadataGenerator.ExtensionsMetadataGenerator.GenerateExtensionsJson(references);

            var root = JObject.Parse(json);
            var extensions = root["extensions"];

            Assert.Equal("Foo", extensions[0]["name"]);
            Assert.Equal(typeof(FooWebJobsStartup).AssemblyQualifiedName, extensions[0]["typeName"]);

            Assert.Equal("BarExtension", extensions[1]["name"]);
            Assert.Equal(typeof(BarWebJobsStartup).AssemblyQualifiedName, extensions[1]["typeName"]);
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
