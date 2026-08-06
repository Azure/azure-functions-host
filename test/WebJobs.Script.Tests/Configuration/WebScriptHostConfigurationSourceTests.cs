// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class WebScriptHostConfigurationSourceTests
    {
        public static IEnumerable<object[]> BranchCases()
        {
            yield return new object[] { true, false, false };
            yield return new object[] { false, true, false };
            yield return new object[] { false, false, true };
            yield return new object[] { false, false, false };
        }

        [Theory]
        [MemberData(nameof(BranchCases))]
        public async Task Load_CapturedBranchRemainsStableWhilePathValuesRefresh(
            bool isAppService,
            bool isLinuxContainer,
            bool isLinuxAppService)
        {
            WebScriptHostConfigurationContractResult result =
                await EnvironmentContractTestHostRunner
                    .RunScenarioAsync<WebScriptHostConfigurationContractResult>(
                        EnvironmentBehaviorParityTestContracts.WebScriptHostConfigurationScenario,
                        $"{isAppService},{isLinuxContainer},{isLinuxAppService}");
            string expectedSelfHost =
                (!isAppService && !isLinuxContainer).ToString();
            string expectedFirstScriptPath = isAppService
                ? Path.Combine("first-home", "site", "wwwroot")
                : "first-root";
            string expectedFirstLogPath = isAppService
                ? Path.Combine("first-home", "LogFiles", "Application", "Functions")
                : "first-logs";
            string expectedSecondScriptPath = isAppService
                ? Path.Combine("second-home", "site", "wwwroot")
                : "second-root";
            string expectedSecondLogPath = isAppService
                ? Path.Combine("second-home", "LogFiles", "Application", "Functions")
                : "second-logs";

            Assert.Equal(expectedSelfHost, result.FirstSelfHost);
            Assert.Equal(expectedFirstScriptPath, result.FirstScriptPath);
            Assert.Equal(expectedFirstLogPath, result.FirstLogPath);
            Assert.Equal(expectedSelfHost, result.SecondSelfHost);
            Assert.Equal(expectedSecondScriptPath, result.SecondScriptPath);
            Assert.Equal(expectedSecondLogPath, result.SecondLogPath);
        }
    }
}
