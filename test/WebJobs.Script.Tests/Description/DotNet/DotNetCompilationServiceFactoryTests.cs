// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class DotNetCompilationServiceFactoryTests
    {
        private readonly ScriptSettingsManager _settingsManager = ScriptSettingsManager.Instance;

        [Fact]
        public void OptimizationLevel_CompilationReleaseModeSetToTrue_ReturnsRelease()
        {
            ResetOptimizationFlag();

            using (var variables = new TestScopedSettings(_settingsManager, EnvironmentSettingNames.CompilationReleaseMode, "True"))
            {
                Assert.Equal(OptimizationLevel.Release, DotNetCompilationServiceFactory.OptimizationLevel);
            }
        }

        [Fact]
        public void OptimizationLevel_CompilationReleaseModeSetToFalse_ReturnsDebug()
        {
            ResetOptimizationFlag();

            using (var variables = new TestScopedSettings(_settingsManager, EnvironmentSettingNames.CompilationReleaseMode, "False"))
            {
                Assert.Equal(OptimizationLevel.Debug, DotNetCompilationServiceFactory.OptimizationLevel);
            }
        }

        [Fact]
        public void OptimizationLevel_WhenRemote_ReturnsRelease()
        {
            ResetOptimizationFlag();

            using (var variables = new TestScopedSettings(_settingsManager, EnvironmentSettingNames.AzureWebsiteInstanceId, "123"))
            {
                Assert.Equal(OptimizationLevel.Release, DotNetCompilationServiceFactory.OptimizationLevel);
            }
        }

        [Fact]
        public void OptimizationLevel_WhenRemoteWithRemoteDebugging_ReturnsDebug()
        {
            ResetOptimizationFlag();

            var variables = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteInstanceId, "123" },
                { EnvironmentSettingNames.RemoteDebuggingPort, "1234" }
            };

            using (new TestScopedSettings(_settingsManager, variables))
            {
                Assert.Equal(OptimizationLevel.Debug, DotNetCompilationServiceFactory.OptimizationLevel);
            }
        }

        private void ResetOptimizationFlag()
        {
            DotNetCompilationServiceFactory.SetOptimizationLevel(null);
        }
    }
}
