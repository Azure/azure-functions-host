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
        public void CreateTokenValidationParameters_HasExpectedAudience(bool isPlaceholderModeEnabled, bool isLinuxConsumptionOnLegion)
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
    }
}
