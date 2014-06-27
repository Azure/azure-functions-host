using System;
using System.IO;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    public class JobHostConfigurationTests
    {
        [Fact]
        public void ConnectionStringProvider_NoDashboardConnectionString_Throw()
        {
            const string DashboardConnectionEnvironmentVariable = "AzureJobsDashboard";
            string previousConnectionString = Environment.GetEnvironmentVariable(DashboardConnectionEnvironmentVariable);

            try
            {
                Environment.SetEnvironmentVariable(DashboardConnectionEnvironmentVariable, null);

                JobHostConfiguration configuration = new JobHostConfiguration
                {
                    StorageConnectionString = "SOME_DATA_CONNECTION_STRING",
                };
                Assert.Null(configuration.DashboardConnectionString); // Guard
                IConnectionStringProvider connectionStringProvider = configuration.GetConnectionStringProvider();

                // Act & Assert
                ExceptionAssert.ThrowsInvalidOperation(() =>
                    connectionStringProvider.GetConnectionString(JobHost.DashboardConnectionStringName),
                    "Failed to validate Microsoft Azure Jobs dashboard connection string: Microsoft Azure Storage account connection string is missing or empty." + Environment.NewLine + "The Microsoft Azure Jobs connection string is specified by setting a connection string named 'AzureJobsDashboard' in the connectionStrings section of the .config file, or with an environment variable named 'AzureJobsDashboard', or by using a constructor for JobHostConfiguration that accepts connection strings.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(DashboardConnectionEnvironmentVariable, previousConnectionString);
            }
        }

        /// <summary>
        /// Checks that we write the marker file when we call the constructor with arguments
        /// </summary>
        [Fact]
        public void TestSdkMarkerIsWrittenWhenInAzureWebSites()
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
