// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class JobHostConfigurationTests
    {
        [Fact]
        public void ConnectionStringProvider_NoDashboardConnectionString_Throw()
        {
            const string DashboardConnectionEnvironmentVariable = "AzureWebJobsDashboard";
            string previousConnectionString = Environment.GetEnvironmentVariable(DashboardConnectionEnvironmentVariable);

            try
            {
                Environment.SetEnvironmentVariable(DashboardConnectionEnvironmentVariable, null);

                JobHostConfiguration configuration = new JobHostConfiguration
                {
                    StorageConnectionString = new CloudStorageAccount(new StorageCredentials("Test", new byte[0], "key") , true).ToString(exportSecrets: true)
                };
                Assert.Null(configuration.DashboardConnectionString); // Guard
                IConnectionStringProvider connectionStringProvider = configuration.GetConnectionStringProvider();

                // Act & Assert
                ExceptionAssert.ThrowsInvalidOperation(() =>
                    connectionStringProvider.GetConnectionString(ConnectionStringNames.Dashboard),
                    "Failed to validate Microsoft Azure WebJobs SDK dashboard connection string: Microsoft Azure Storage account connection string is missing or empty." + Environment.NewLine + "The Microsoft Azure WebJobs SDK connection string is specified by setting a connection string named 'AzureWebJobsDashboard' in the connectionStrings section of the .config file, or with an environment variable named 'AzureWebJobsDashboard', or through JobHostConfiguration.");
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
