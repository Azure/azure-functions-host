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
                "The storage emulator is not supported, please use a storage account hosted in Windows Azure.");
        }

        [TestMethod]
        public void ValidateConnectionString_WithProxiedEmulator_Throws()
        {
            ExceptionAssert.ThrowsInvalidOperation(
                () => Utility.ValidateConnectionString("UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://myProxyUri"),
                "The storage emulator is not supported, please use a storage account hosted in Windows Azure.");
        }
    }
}