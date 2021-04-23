// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Management
{
    public class RunFromPackageContextTests
    {
        private const string Url = "http://url/file.zip";

        [Theory]
        [InlineData(EnvironmentSettingNames.ScmRunFromPackage, Url, true, true)]
        [InlineData(EnvironmentSettingNames.ScmRunFromPackage, Url, false, false)]
        [InlineData(EnvironmentSettingNames.ScmRunFromPackage, "1", true, false)]
        [InlineData(EnvironmentSettingNames.ScmRunFromPackage, "1", false, false)]
        [InlineData(EnvironmentSettingNames.ScmRunFromPackage, "", true, false)]
        [InlineData(EnvironmentSettingNames.ScmRunFromPackage, "", false, false)]
        [InlineData(EnvironmentSettingNames.ScmRunFromPackage, null, true, false)]
        [InlineData(EnvironmentSettingNames.ScmRunFromPackage, null, false, false)]
        [InlineData(EnvironmentSettingNames.AzureWebsiteRunFromPackage, Url, true, true)]
        [InlineData(EnvironmentSettingNames.AzureWebsiteRunFromPackage, Url, false, true)]
        [InlineData(EnvironmentSettingNames.AzureWebsiteRunFromPackage, "1", true, false)]
        [InlineData(EnvironmentSettingNames.AzureWebsiteRunFromPackage, "1", false, false)]
        [InlineData(EnvironmentSettingNames.AzureWebsiteRunFromPackage, "", true, false)]
        [InlineData(EnvironmentSettingNames.AzureWebsiteRunFromPackage, "", false, false)]
        [InlineData(EnvironmentSettingNames.AzureWebsiteRunFromPackage, null, true, false)]
        [InlineData(EnvironmentSettingNames.AzureWebsiteRunFromPackage, null, false, false)]
        public async Task Returns_IsRunFromPackage(string environmentVariableName, string url, bool blobExists, bool scmRunFromPackageConfigured)
        {
            var cloudBlockBlobService = new Mock<RunFromPackageCloudBlockBlobService>(MockBehavior.Strict);
            cloudBlockBlobService
                .Setup(c => c.BlobExists(Url, environmentVariableName, NullLogger.Instance))
                .ReturnsAsync(blobExists);

            cloudBlockBlobService
                .Setup(c => c.BlobExists(It.Is<string>(s => !string.Equals(s, Url, StringComparison.OrdinalIgnoreCase)), environmentVariableName, NullLogger.Instance))
                .ReturnsAsync(false);

            var runFromPackageContext = new RunFromPackageContext(environmentVariableName, url, 10,
                false, cloudBlockBlobService.Object);
            Assert.Equal(scmRunFromPackageConfigured, await runFromPackageContext.IsRunFromPackage(NullLogger.Instance));
        }

        [Theory]
        [InlineData(EnvironmentSettingNames.ScmRunFromPackage, true)]
        [InlineData(EnvironmentSettingNames.AzureWebsiteRunFromPackage, false)]
        [InlineData(EnvironmentSettingNames.AzureWebsiteZipDeployment, false)]
        [InlineData(EnvironmentSettingNames.AzureWebsiteAltZipDeployment, false)]
        public void Returns_ScmRunFromPackage(string environmentVariableName, bool isScmRunFromPackage)
        {
            var runFromPackageContext = new RunFromPackageContext(environmentVariableName, "", 0,  false);
            Assert.Equal(isScmRunFromPackage, runFromPackageContext.IsScmRunFromPackage());
        }
    }
}
