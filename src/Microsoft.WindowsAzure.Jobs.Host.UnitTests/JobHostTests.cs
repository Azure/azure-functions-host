using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs.Test;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests
{
    [TestClass]
    public class JobHostTests
    {
        [TestMethod]
        public void DefaultConstructor_NoRuntimeConnectionString_Throw()
        {
            var existingValue = Environment.GetEnvironmentVariable(JobHost.LoggingConnectionStringName, EnvironmentVariableTarget.Process);

            try
            {
                Environment.SetEnvironmentVariable(JobHost.LoggingConnectionStringName, null);

                ExceptionAssert.ThrowsInvalidOperation(() => new JobHost(), "Windows Azure Jobs runtime connection string is missing. You can specify it by setting a connection string named 'AzureJobsRuntime' in the connectionStrings section of the .config file, or with an environment variable named 'AzureJobsRuntime', or by using the constructor for JobHost that accepts connection strings.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(JobHost.LoggingConnectionStringName, existingValue);
            }
        }
    }
}
