using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ExtensionsMetadataGeneratorTests
{
    public class ExtensionsMetadataGeneratorTests
    {
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
