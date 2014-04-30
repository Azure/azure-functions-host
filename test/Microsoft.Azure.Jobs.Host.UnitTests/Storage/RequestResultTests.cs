using Microsoft.Azure.Jobs.Host.Storage;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Storage
{
    public class RequestResultTests
    {
        [Fact]
        public void HttpStatusCode_IsSpecifiedValue()
        {
            // Arrange
            int expectedStatusCode = 101;
            CloudRequestResult product = CreateProductUnderTest(expectedStatusCode);

            // Act
            int statusCode = product.HttpStatusCode;

            Assert.Equal(expectedStatusCode, statusCode);
        }

        private static CloudRequestResult CreateProductUnderTest(int httpStatusCode)
        {
            return new CloudRequestResult(httpStatusCode);
        }
    }
}
