// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
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
                IStorageAccountProvider storageAccountProvider = configuration.GetStorageAccountProvider();

                // Act & Assert
                ExceptionAssert.ThrowsInvalidOperation(() =>
                    storageAccountProvider.GetDashboardAccountAsync(CancellationToken.None).GetAwaiter().GetResult(),
                    "Microsoft Azure WebJobs SDK Dashboard connection string is missing or empty. The Microsoft Azure Storage account connection string can be set in the following ways:" + Environment.NewLine +
                    "1. Set the connection string named 'AzureWebJobsDashboard' in the connectionStrings section of the .config file in the following format " +
                    "<add name=\"AzureWebJobsDashboard\" connectionString=\"DefaultEndpointsProtocol=http|https;AccountName=NAME;AccountKey=KEY\" />, or" + Environment.NewLine +
                    "2. Set the environment variable named 'AzureWebJobsDashboard', or" + Environment.NewLine +
                    "3. Set corresponding property of JobHostConfiguration.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(DashboardConnectionEnvironmentVariable, previousConnectionString);
            }
        }

        [Fact]
        public void HostId_IfNull_DoesNotThrow()
        {
            // Arrange
            JobHostConfiguration configuration = new JobHostConfiguration();
            string hostId = null;

            // Act & Assert
            Assert.DoesNotThrow(() => { configuration.HostId = hostId; });
        }

        [Fact]
        public void HostId_IfValid_DoesNotThrow()
        {
            // Arrange
            JobHostConfiguration configuration = new JobHostConfiguration();
            string hostId = "abc";

            // Act & Assert
            Assert.DoesNotThrow(() => { configuration.HostId = hostId; });
        }

        [Fact]
        public void HostId_IfMinimumLength_DoesNotThrow()
        {
            // Arrange
            JobHostConfiguration configuration = new JobHostConfiguration();
            string hostId = "a";

            // Act & Assert
            Assert.DoesNotThrow(() => { configuration.HostId = hostId; });
        }

        [Fact]
        public void HostId_IfMaximumLength_DoesNotThrow()
        {
            // Arrange
            JobHostConfiguration configuration = new JobHostConfiguration();
            const int maximumValidCharacters = 32;
            string hostId = new string('a', maximumValidCharacters);

            // Act & Assert
            Assert.DoesNotThrow(() => { configuration.HostId = hostId; });
        }

        [Fact]
        public void HostId_IfContainsEveryValidLetter_DoesNotThrow()
        {
            // Arrange
            JobHostConfiguration configuration = new JobHostConfiguration();
            string hostId = "abcdefghijklmnopqrstuvwxyz";

            // Act & Assert
            Assert.DoesNotThrow(() => { configuration.HostId = hostId; });
        }

        [Fact]
        public void HostId_IfContainsEveryValidOtherCharacter_DoesNotThrow()
        {
            // Arrange
            JobHostConfiguration configuration = new JobHostConfiguration();
            string hostId = "0-123456789";

            // Act & Assert
            Assert.DoesNotThrow(() => { configuration.HostId = hostId; });
        }

        [Fact]
        public void HostId_IfEmpty_Throws()
        {
            TestHostIdThrows(String.Empty);
        }

        [Fact]
        public void HostId_IfTooLong_Throws()
        {
            const int maximumValidCharacters = 32;
            string hostId = new string('a', maximumValidCharacters + 1);
            TestHostIdThrows(hostId);
        }

        [Fact]
        public void HostId_IfContainsInvalidCharacter_Throws()
        {
            // Uppercase character are not allowed.
            TestHostIdThrows("aBc");
        }

        [Fact]
        public void HostId_IfStartsWithDash_Throws()
        {
            TestHostIdThrows("-abc");
        }

        [Fact]
        public void HostId_IfEndsWithDash_Throws()
        {
            TestHostIdThrows("abc-");
        }

        [Fact]
        public void HostId_IfContainsConsecutiveDashes_Throws()
        {
            TestHostIdThrows("a--bc");
        }

        private static void TestHostIdThrows(string hostId)
        {
            // Arrange
            JobHostConfiguration configuration = new JobHostConfiguration();

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => { configuration.HostId = hostId; }, "value",
                "A host ID must be between 1 and 32 characters, contain only lowercase letters, numbers, and " +
                "dashes, not start or end with a dash, and not contain consecutive dashes.");
        }
    }
}
