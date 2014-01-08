using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs.Test;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests
{
    [TestClass]
    public class UtilityTests
    {
        [TestMethod]
        public void ValidateConnectionString_WithEmulator_Throws()
        {
            ExceptionAssert.ThrowsInvalidOperation(
                () => Utility.ValidateConnectionString("UseDevelopmentStorage=true"),
                "The Windows Azure Storage Emulator is not supported, please use a Windows Azure Storage account hosted in Windows Azure.");
        }

        [TestMethod]
        public void ValidateConnectionString_WithProxiedEmulator_Throws()
        {
            ExceptionAssert.ThrowsInvalidOperation(
                () => Utility.ValidateConnectionString("UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://myProxyUri"),
                "The Windows Azure Storage Emulator is not supported, please use a Windows Azure Storage account hosted in Windows Azure.");
        }

        [TestMethod]
        public void ValidateConnectionString_WithEmpty_Throws()
        {
            ExceptionAssert.ThrowsInvalidOperation(
                () => Utility.ValidateConnectionString(string.Empty),
                "Windows Azure Storage account connection string value is missing.");
        }

        [TestMethod]
        public void ValidateConnectionString_WithNull_Throws()
        {
            ExceptionAssert.ThrowsInvalidOperation(
                () => Utility.ValidateConnectionString(null),
                "Windows Azure Storage account connection string is missing.");
        }
    }
}