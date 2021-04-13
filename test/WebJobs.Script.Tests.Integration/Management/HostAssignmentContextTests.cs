// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    public class HostAssignmentContextTests
    {
        [Theory]
        [InlineData(null, null, false)]
        [InlineData("", "", false)]
        [InlineData("secret", "", false)]
        [InlineData("", "endpoint", false)]
        [InlineData("secret", "endpoint", true)]
        public void IsMSIEnabled_ReturnsMSIEndpointAndMSIStatus(string msiSecret, string msiEndpoint, bool expectedMsiEnabled)
        {
            var hostAssignmentContext = new HostAssignmentContext();
            hostAssignmentContext.Environment = new Dictionary<string, string>();
            hostAssignmentContext.Environment[EnvironmentSettingNames.MsiSecret] = msiSecret;
            hostAssignmentContext.Environment[EnvironmentSettingNames.MsiEndpoint] = msiEndpoint;

            string actualMsiEndpoint;
            var actualMsiEnabled = hostAssignmentContext.IsMSIEnabled(out actualMsiEndpoint);
            Assert.Equal(expectedMsiEnabled, actualMsiEnabled);
            Assert.Equal(msiEndpoint, actualMsiEndpoint);
        }

        [Fact]
        public void Returns_BYOS_EnvironmentVariables()
        {
            var hostAssignmentContext = new HostAssignmentContext()
            {
                Environment = new Dictionary<string, string>
                {
                    [EnvironmentSettingNames.MsiSecret] = "secret",
                    ["AZUREFILESSTORAGE_storage1"] = "storage1",
                    ["AzureFilesStorage_storage2"] = "storage2",
                    ["AZUREBLOBSTORAGE_blob1"] = "blob1",
                    ["AzureBlobStorage_blob2"] = "blob2",
                    [EnvironmentSettingNames.MsiEndpoint] = "endpoint",
                }
            };

            var byosEnvironmentVariables = hostAssignmentContext.GetBYOSEnvironmentVariables();
            Assert.Equal(4, byosEnvironmentVariables.Count());

            Assert.Equal("storage1",byosEnvironmentVariables.First(env => env.Key == "AZUREFILESSTORAGE_storage1").Value);
            Assert.Equal("storage2", byosEnvironmentVariables.First(env => env.Key == "AzureFilesStorage_storage2").Value);
            Assert.Equal("blob1", byosEnvironmentVariables.First(env => env.Key == "AZUREBLOBSTORAGE_blob1").Value);
            Assert.Equal("blob2", byosEnvironmentVariables.First(env => env.Key == "AzureBlobStorage_blob2").Value);
        }

        [Theory]
        [InlineData("", "",  false)]
        [InlineData("cs", "share",  true)]
        [InlineData("cs", "",  false)]
        [InlineData("cs", null,  false)]
        [InlineData("", "share",  false)]
        [InlineData(null, "share",  false)]
        [InlineData(null, null,  false)]
        public void Returns_Is_AzureFilesConfigured(string connectionString, string contentShare, bool isAzureFilesConfigured)
        {
            var hostAssignmentContext = new HostAssignmentContext()
            {
                Environment = new Dictionary<string, string>()
            };

            hostAssignmentContext.Environment[EnvironmentSettingNames.AzureFilesConnectionString] = connectionString;
            hostAssignmentContext.Environment[EnvironmentSettingNames.AzureFilesContentShare] = contentShare;

            Assert.Equal(isAzureFilesConfigured, hostAssignmentContext.IsAzureFilesContentShareConfigured(NullLogger.Instance));
        }
    }
}
