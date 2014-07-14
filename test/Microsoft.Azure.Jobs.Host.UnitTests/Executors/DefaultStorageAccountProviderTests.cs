using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.WindowsAzure.Storage;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Executors
{
    public class DefaultStorageAccountProviderTests
    {
        [Fact]
        public void TryValidateConnectionString_WithEmulator_Fails()
        {
            string connectionString = "UseDevelopmentStorage=true";
            var expectedErrorMessage = "The Microsoft Azure Storage Emulator is not supported, please use a Microsoft Azure Storage account hosted in Microsoft Azure.";
            
            string validationErrorMessage;
            CloudStorageAccount ignore;
            bool result = DefaultStorageAccountProvider.TryParseAndValidateAccount(connectionString, out ignore, out validationErrorMessage);

            Assert.False(result);
            Assert.Equal(validationErrorMessage, expectedErrorMessage);
        }

        [Fact]
        public void TryValidateConnectionString_WithProxiedEmulator_Fails()
        {
            string connectionString = "UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://myProxyUri";
            var expectedErrorMessage = "The Microsoft Azure Storage Emulator is not supported, please use a Microsoft Azure Storage account hosted in Microsoft Azure.";

            string validationErrorMessage;
            CloudStorageAccount ignore;
            bool result = DefaultStorageAccountProvider.TryParseAndValidateAccount(connectionString, out ignore, out validationErrorMessage);

            Assert.False(result);
            Assert.Equal(validationErrorMessage, expectedErrorMessage);
        }

        [Fact]
        public void TryValidateConnectionString_WithEmpty_Fails()
        {
            string connectionString = string.Empty;
            var expectedErrorMessage = "Microsoft Azure Storage account connection string is missing or empty.";

            string validationErrorMessage;
            CloudStorageAccount ignore;
            bool result = DefaultStorageAccountProvider.TryParseAndValidateAccount(connectionString, out ignore, out validationErrorMessage);

            Assert.False(result);
            Assert.Equal(validationErrorMessage, expectedErrorMessage);
        }

        [Fact]
        public void TryValidateConnectionString_WithNull_Fails()
        {
            string connectionString = null;
            var expectedErrorMessage = "Microsoft Azure Storage account connection string is missing or empty.";

            string validationErrorMessage;
            CloudStorageAccount ignore;
            bool result = DefaultStorageAccountProvider.TryParseAndValidateAccount(connectionString, out ignore, out validationErrorMessage);

            Assert.False(result);
            Assert.Equal(validationErrorMessage, expectedErrorMessage);
        }
    }
}
