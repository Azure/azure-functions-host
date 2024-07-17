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
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void CreateTokenValidationParameters_HasExpectedAudiences(bool isPlaceholderModeEnabled, bool isLinuxConsumptionOnLegion)
        {
            var podName = "RandomPodName";
            var containerName = "RandomContainerName";
            var siteName = "RandomSiteName";

            var expectedWithSiteName = new string[]
            {
                string.Format(ScriptConstants.SiteAzureFunctionsUriFormat, siteName),
                string.Format(ScriptConstants.SiteUriFormat, siteName)
            };
            var expectedWithPodName = new string[] { podName };
            var expectedWithContainerName = Array.Empty<string>();

            var testData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AzureWebsiteName] = siteName,
                [WebsitePodName] = podName,
                [ContainerName] = string.Empty
            };

            if (isPlaceholderModeEnabled)
            {
                testData[AzureWebsitePlaceholderMode] = "1";
            }

            if (isLinuxConsumptionOnLegion)
            {
                testData[AzureWebsiteInstanceId] = string.Empty;
                testData[WebsitePodName] = podName;
                testData[LegionServiceHost] = "1";
            }
            else
            {
                expectedWithContainerName = new string[] { containerName };
                testData[AzureWebsiteInstanceId] = string.Empty;
                testData[ContainerName] = containerName;
            }

            testData[ContainerEncryptionKey] = Convert.ToBase64String(TestHelpers.GenerateKeyBytes());
            using (new TestScopedSettings(ScriptSettingsManager.Instance, testData))
            {
                var tokenValidationParameters = ScriptJwtBearerExtensions.CreateTokenValidationParameters();
                var audiences = tokenValidationParameters.ValidAudiences.ToList();

                if (isPlaceholderModeEnabled &&
                    isLinuxConsumptionOnLegion)
                {
                    Assert.Equal(audiences.Count, expectedWithPodName.Length);
                    Assert.Equal(audiences[0], expectedWithPodName[0]);
                }
                else if (isPlaceholderModeEnabled)
                {
                    Assert.Equal(audiences.Count, expectedWithContainerName.Length);
                    Assert.Equal(audiences[0], expectedWithContainerName[0]);
                }
                else
                {
                    Assert.Equal(audiences.Count, expectedWithSiteName.Length);
                    Assert.True(audiences.Contains(expectedWithSiteName[0]));
                    Assert.True(audiences.Contains(expectedWithSiteName[1]));
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
