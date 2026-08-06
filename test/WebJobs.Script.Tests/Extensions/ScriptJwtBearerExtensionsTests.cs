// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Config.Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class ScriptJwtBearerExtensionsTests
    {
        private static readonly ScriptSettingsManager SettingsManager =
            ScriptSettingsManager.Instance;

        [Theory]
        [InlineData(true, "FlexConsumption", null, "1", "RandomPodName", "", "RandomPodName")] // Placeholder mode Flex Consumption
        [InlineData(true, "Dynamic", null, "1", null, "RandomContainerName", "RandomContainerName")] // Placeholder mode Linux Consumption on Legion
        [InlineData(true, "Dynamic", null, null, null, "RandomContainerName", "RandomContainerName")] // Placeholder mode Linux Consumption on Atlas
        [InlineData(false, "FlexConsumption", null, "1", "RandomPodName", null, "https://RandomSiteName.azurewebsites.net/azurefunctions,https://RandomSiteName.azurewebsites.net")]
        [InlineData(false, "Dynamic", null, null, null, "RandomContainerName", "https://RandomSiteName.azurewebsites.net/azurefunctions,https://RandomSiteName.azurewebsites.net")]
        [InlineData(false, "Dynamic", "123", null, null, null, "https://RandomSiteName.azurewebsites.net/azurefunctions,https://RandomSiteName.azurewebsites.net")]
        public void CreateTokenValidationParameters_HasExpectedAudiences(bool isPlaceholderModeEnabled, string sku,
            string websiteInstanceId, string legionServiceHost, string podName, string containerName, string expectedAudiences)
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
            using (new TestScopedSettings(SettingsManager, testData))
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

        [Fact]
        public void CreateTokenValidationParameters_AllProfileInputsRemainLateBoundAcrossPhases()
        {
            foreach (EnvironmentProfileContract profile in
                EnvironmentBehaviorParityFixtures.CompleteProfiles)
            {
                foreach (HostPhase phase in Enum.GetValues<HostPhase>())
                {
                    Dictionary<string, string> settings = new(
                        profile.Markers,
                        StringComparer.OrdinalIgnoreCase)
                    {
                        [AzureWebsitePlaceholderMode] =
                            EnvironmentBehaviorParityFixtures.IsPlaceholderPhase(phase)
                                ? "1"
                                : "0",
                        [ContainerEncryptionKey] =
                            Convert.ToBase64String(TestHelpers.GenerateKeyBytes())
                    };
                    bool appInputsAvailable =
                        phase != HostPhase.PlaceholderBeforeAssignment;
                    if (appInputsAvailable)
                    {
                        settings[AzureWebsiteName] = "ParitySite";
                        settings[AzureWebsiteRuntimeSiteName] = "ParityRuntime";
                    }

                    using (new TestScopedSettings(SettingsManager, settings))
                    {
                        string[] actual = ScriptJwtBearerExtensions
                            .CreateTokenValidationParameters()
                            .ValidAudiences
                            .ToArray();
                        string[] expected = GetExpectedAudiences(
                            profile.Profile,
                            phase,
                            appInputsAvailable);

                        Assert.Equal(expected, actual);
                    }
                }
            }
        }

        [Fact]
        public async Task JwtSpecializationLatch_ReplacesSnapshotOnce()
        {
            JwtLatchContractResult result =
                await EnvironmentContractTestHostRunner.RunScenarioAsync<JwtLatchContractResult>(
                    EnvironmentBehaviorParityTestContracts.JwtLatchScenario);

            Assert.Equal(["placeholder-pod"], result.PlaceholderAudiences);
            Assert.Equal(
                [
                    "https://specialized-site.azurewebsites.net/azurefunctions",
                        "https://specialized-site.azurewebsites.net",
                        "https://specialized-runtime.azurewebsites.net"
                ],
                result.SpecializedAudiences);
            Assert.Equal(
                result.SpecializedAudiences,
                result.AudiencesAfterSecondMutation);
        }

        private static string[] GetExpectedAudiences(
            HostingEnvironmentProfile profile,
            HostPhase phase,
            bool appInputsAvailable)
        {
            if (EnvironmentBehaviorParityFixtures.IsPlaceholderPhase(phase))
            {
                string[] placeholderAudiences = profile switch
                {
                    HostingEnvironmentProfile.FlexConsumptionLegion => ["flex-pod"],
                    HostingEnvironmentProfile.LinuxConsumptionAtlas => ["atlas-container"],
                    HostingEnvironmentProfile.LinuxConsumptionLegion => ["legion-container"],
                    _ => null
                };
                if (placeholderAudiences is not null)
                {
                    return placeholderAudiences;
                }
            }

            return appInputsAvailable
                ?
                [
                    "https://ParitySite.azurewebsites.net/azurefunctions",
                        "https://ParitySite.azurewebsites.net",
                        "https://ParityRuntime.azurewebsites.net"
                ]
                :
                [
                    "https://.azurewebsites.net/azurefunctions",
                        "https://.azurewebsites.net"
                ];
        }
    }
}
