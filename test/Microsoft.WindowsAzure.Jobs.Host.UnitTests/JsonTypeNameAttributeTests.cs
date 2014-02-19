using Microsoft.WindowsAzure.Jobs.Host.TestCommon;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests
{
    public class JsonTypeNameAttributeTests
    {
        [Fact]
        public static void Constructor_IfTypeNameIsNull_Throws()
        {
            // Arrange
            string typeName = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => CreateProductUnderTest(typeName), "typeName");
        }

        [Fact]
        public static void TypeName_IsSpecifiedInstance()
        {
            // Arrange
            string expectedTypeName = "IgnoreName";
            JsonTypeNameAttribute product = CreateProductUnderTest(expectedTypeName);

            // Act
            string typeName = product.TypeName;

            // Assert
            Assert.Same(expectedTypeName, typeName);
        }

        private static JsonTypeNameAttribute CreateProductUnderTest(string typeName)
        {
            return new JsonTypeNameAttribute(typeName);
        }
    }
}
