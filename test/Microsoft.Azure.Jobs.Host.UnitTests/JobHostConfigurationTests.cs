using System;
using System.IO;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    public class JobHostConfigurationTests
    {
        [Fact]
        public void ConnectionStringProvider_NoRuntimeConnectionString_Throw()
        {
            JobHostConfiguration configuration = new JobHostConfiguration
            {
                DataConnectionString = "SOME_DATA_CONNECTION_STRING",
            };
            Assert.Null(configuration.RuntimeConnectionString); // Guard
            IConnectionStringProvider connectionStringProvider = configuration.GetConnectionStringProvider();

            // Act & Assert
            ExceptionAssert.ThrowsInvalidOperation(() => 
                connectionStringProvider.GetConnectionString(JobHost.LoggingConnectionStringName), 
                "Failed to validate Microsoft Azure Jobs runtime connection string: Microsoft Azure Storage account connection string is missing or empty." + Environment.NewLine + "The Microsoft Azure Jobs connection string is specified by setting a connection string named 'AzureJobsRuntime' in the connectionStrings section of the .config file, or with an environment variable named 'AzureJobsRuntime', or by using a constructor for JobHostConfiguration that accepts connection strings.");
        }

        /// <summary>
        /// Checks that we write the marker file when we call the constructor with arguments
        /// </summary>
        [Fact]
        public void TestSdkMarkerIsWrittenWhenInAntares()
        {
            // Arrange
            string tempDir = Path.GetTempPath();
            const string filename = "WebJobsSdk.marker";

            var path = Path.Combine(tempDir, filename);

            File.Delete(path);


            try
            {
                Environment.SetEnvironmentVariable(WebSitesKnownKeyNames.JobDataPath, tempDir);

                // Act
                JobHostConfiguration configuration = new JobHostConfiguration();

                // Assert
                Assert.True(File.Exists(path), "SDK marker file should have been written");
            }
            finally
            {
                Environment.SetEnvironmentVariable(WebSitesKnownKeyNames.JobDataPath, null);
                File.Delete(path);
            }
        }
    }
}
