// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class ScriptJwtBearerExtensionsTests
    {
        [Theory]
        [InlineData(true, "Dynamic", null, "1", null, "RandomContainerName", "RandomContainerName")] // Placeholder mode Linux Consumption on Legion
        [InlineData(true, "Dynamic", null, null, null, "RandomContainerName", "RandomContainerName")] // Placeholder mode Linux Consumption on Atlas
        [InlineData(false, "Dynamic", null, null, null, "RandomContainerName", "https://RandomSiteName.azurewebsites.net/azurefunctions,https://RandomSiteName.azurewebsites.net")]
        [InlineData(false, "Dynamic", "123", null, null, null, "https://RandomSiteName.azurewebsites.net/azurefunctions,https://RandomSiteName.azurewebsites.net")]
        public void CreateTokenValidationParameters_HasExpectedAudiences(bool isPlaceholderModeEnabled, string sku, string websiteInstanceId,
            string legionServiceHost, string podName, string containerName, string expectedAudiences)
        {
            var siteName = "RandomSiteName";
            var testData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AzureWebsiteName] = siteName,
                [WebsitePodName] = podName,
                [ContainerName] = containerName,
                [AzureWebsiteSku] = sku,
                [LegionServiceHost] = legionServiceHost,
                [AzureWebsiteInstanceId] = websiteInstanceId
            };

            if (isPlaceholderModeEnabled)
            {
                testData[AzureWebsitePlaceholderMode] = "1";
            }

            testData[ContainerEncryptionKey] = Convert.ToBase64String(TestHelpers.GenerateKeyBytes());
            using (new TestScopedSettings(ScriptSettingsManager.Instance, testData))
            {
                var tokenValidationParameters = ScriptJwtBearerExtensions.CreateTokenValidationParameters();
                var audiences = tokenValidationParameters.ValidAudiences.ToList();

                string[] parsedAudiences = expectedAudiences.Split(',');
                Assert.Equal(audiences.Count, parsedAudiences.Length);
                for (int i = 0; i < audiences.Count; i++)
                {
                    Assert.Equal(audiences[i], parsedAudiences[i]);
                }
            }
        }

        [Theory]
        [InlineData("testsite", "testsite")]
        [InlineData("testsite", "testsite__5bb5")]
        [InlineData("testsite", null)]
        [InlineData("testsite", "")]
        public void CreateTokenValidationParameters_NonProductionSlot_HasExpectedAudiences(string siteName, string runtimeSiteName)
        {
            string azFuncAudience = string.Format(ScriptConstants.SiteAzureFunctionsUriFormat, siteName);
            string siteAudience = string.Format(ScriptConstants.SiteUriFormat, siteName);
            string runtimeSiteAudience = string.Format(ScriptConstants.SiteUriFormat, runtimeSiteName);

            var testEnv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { EnvironmentSettingNames.AzureWebsiteName, siteName },
                { EnvironmentSettingNames.AzureWebsiteRuntimeSiteName, runtimeSiteName },
                { ContainerEncryptionKey, Convert.ToBase64String(TestHelpers.GenerateKeyBytes()) }
            };

            using (new TestScopedSettings(ScriptSettingsManager.Instance, testEnv))
            {
                var tokenValidationParameters = ScriptJwtBearerExtensions.CreateTokenValidationParameters();
                var audiences = tokenValidationParameters.ValidAudiences.ToArray();

                Assert.Equal(audiences[0], azFuncAudience);
                Assert.Equal(audiences[1], siteAudience);

                if (string.Compare(siteName, runtimeSiteName, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    Assert.Equal(2, audiences.Length);
                }
                else if (!string.IsNullOrEmpty(runtimeSiteName))
                {
                    Assert.Equal(3, audiences.Length);
                    Assert.Equal(audiences[2], runtimeSiteAudience);
                }
            }
        }
    }
}
