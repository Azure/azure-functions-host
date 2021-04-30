// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging.Abstractions;
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
        public void Returns_IsRunFromPackage(string environmentVariableName, string url, bool blobExists, bool scmRunFromPackageConfigured)
        {
            var runFromPackageContext = new RunFromPackageContext(environmentVariableName, url, 10, false);
            var options = new ScriptApplicationHostOptions
            {
                IsScmRunFromPackage = Url.Equals(url) ? blobExists : false
            };
            Assert.Equal(scmRunFromPackageConfigured, runFromPackageContext.IsRunFromPackage(options, NullLogger.Instance));
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
