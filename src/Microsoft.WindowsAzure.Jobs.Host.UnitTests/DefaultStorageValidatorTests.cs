using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests
{
    [TestClass]
    public class DefaultStorageValidatorTests
    {
        [TestMethod]
        public void TryValidateConnectionString_WithEmulator_Fails()
        {
            var validator = new DefaultStorageValidator();
            string connectionString = "UseDevelopmentStorage=true";
            var expectedErrorMessage = "The Windows Azure Storage Emulator is not supported, please use a Windows Azure Storage account hosted in Windows Azure.";
            
            string validationErrorMessage;
            bool result = validator.TryValidateConnectionString(connectionString, out validationErrorMessage);

            Assert.IsFalse(result);
            Assert.AreEqual(validationErrorMessage, expectedErrorMessage);
        }

        [TestMethod]
        public void TryValidateConnectionString_WithProxiedEmulator_Fails()
        {
            var validator = new DefaultStorageValidator();
            string connectionString = "UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://myProxyUri";
            var expectedErrorMessage = "The Windows Azure Storage Emulator is not supported, please use a Windows Azure Storage account hosted in Windows Azure.";

            string validationErrorMessage;
            bool result = validator.TryValidateConnectionString(connectionString, out validationErrorMessage);

            Assert.IsFalse(result);
            Assert.AreEqual(validationErrorMessage, expectedErrorMessage);
        }

        [TestMethod]
        public void TryValidateConnectionString_WithEmpty_Fails()
        {
            var validator = new DefaultStorageValidator();
            string connectionString = string.Empty;
            var expectedErrorMessage = "Windows Azure Storage account connection string is missing or empty.";

            string validationErrorMessage;
            bool result = validator.TryValidateConnectionString(connectionString, out validationErrorMessage);

            Assert.IsFalse(result);
            Assert.AreEqual(validationErrorMessage, expectedErrorMessage);
        }

        [TestMethod]
        public void TryValidateConnectionString_WithNull_Fails()
        {
            var validator = new DefaultStorageValidator();
            string connectionString = null;
            var expectedErrorMessage = "Windows Azure Storage account connection string is missing or empty.";

            string validationErrorMessage;
            bool result = validator.TryValidateConnectionString(connectionString, out validationErrorMessage);

            Assert.IsFalse(result);
            Assert.AreEqual(validationErrorMessage, expectedErrorMessage);
        }
    }
}