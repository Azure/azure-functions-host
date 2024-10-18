﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
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
        [InlineData("testsite", "testsite", "https://testsite.azurewebsites.net,https://testsite.azurewebsites.net/azurefunctions")]
        [InlineData("testsite", "testsite__5bb5", "https://testsite.azurewebsites.net,https://testsite.azurewebsites.net/azurefunctions,https://testsite__5bb5.azurewebsites.net")]
        [InlineData("testsite", null, "https://testsite.azurewebsites.net,https://testsite.azurewebsites.net/azurefunctions")]
        [InlineData("testsite", "", "https://testsite.azurewebsites.net,https://testsite.azurewebsites.net/azurefunctions")]
        [InlineData("testsite__5bb5", null, "https://testsite__5bb5.azurewebsites.net,https://testsite__5bb5.azurewebsites.net/azurefunctions,https://testsite.azurewebsites.net")]
        [InlineData("testsite__5bb5", "testsite__5bb5", "https://testsite__5bb5.azurewebsites.net,https://testsite__5bb5.azurewebsites.net/azurefunctions,https://testsite.azurewebsites.net")]
        [InlineData("testsite__5bb5", "testsite", "https://testsite__5bb5.azurewebsites.net,https://testsite__5bb5.azurewebsites.net/azurefunctions,https://testsite.azurewebsites.net")]
        public void CreateTokenValidationParameters_NonProductionSlot_HasExpectedAudiences(string siteName, string runtimeSiteName, string expectedAudienceList)
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
                string[] audiences = tokenValidationParameters.ValidAudiences.ToArray();

                string[] expectedAudiences = expectedAudienceList.Split(',').ToArray();
                Assert.Equal(expectedAudiences, audiences);
            }
        }

        [Theory]
        [InlineData("testsite", null, "https://testsite.azurewebsites.net", true)]
        [InlineData("testsite", "testsite", "https://testsite.azurewebsites.net", true)]
        [InlineData("testsite", "testsite__5bb5", "https://testsite__5bb5.azurewebsites.net", true)]
        [InlineData("testsite", null, "https://testsite__5bb5.azurewebsites.net", true)]
        [InlineData("testsite__5bb5", "testsite", "https://testsite.azurewebsites.net", true)]
        [InlineData("testsite__5bb5", null, "https://testsite.azurewebsites.net", true)]
        [InlineData("testsite__5bb5", "testsite__5bb5", "https://testsite.azurewebsites.net", true)]
        [InlineData("testsite__5bb5", null, "https://testsite__5bb5.azurewebsites.net", true)]
        [InlineData("testsite__5bb5", "testsite__5bb5", "https://testsite__5bb5.azurewebsites.net", true)]
        [InlineData("c", null, "https://a.azurewebsites.net,https://b.azurewebsites.net,https://c.azurewebsites.net", true)]
        [InlineData("d", null, "https://a.azurewebsites.net,https://b.azurewebsites.net,https://c.azurewebsites.net", false)]
        public void IssuerValidator_PerformsExpectedValidations(string siteName, string runtimeSiteName, string audienceList, bool expected)
        {
            string[] audiences = audienceList.Split(",");
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

                SecurityToken securityToken = null;
                bool result = ScriptJwtBearerExtensions.AudienceValidator(audiences, securityToken, tokenValidationParameters);

                Assert.Equal(expected, result);
            }
        }
    }
}
