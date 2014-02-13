using Microsoft.WindowsAzure.Jobs.Host.Storage;
using Microsoft.WindowsAzure.Jobs.Host.TestCommon;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests.Storage
{
    public class StorageExceptionTests
    {
        [Fact]
        public void RequestInformation_IsSpecifiedInstance()
        {
            // Arrange
            CloudRequestResult expectedRequestInformation = CreateRequest();
            CloudStorageException product = CreateProductUnderTest(expectedRequestInformation);

            // Act
            CloudRequestResult requestInformation = product.RequestInformation;

            // Assert
            Assert.Same(expectedRequestInformation, requestInformation);
        }

        private static CloudStorageException CreateProductUnderTest(CloudRequestResult requestInformation)
        {
            return new CloudStorageException(requestInformation);
        }

        private static CloudRequestResult CreateRequest()
        {
            return new CloudRequestResult(default(int));
        }
    }
}
