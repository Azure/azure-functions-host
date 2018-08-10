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
    }
}
