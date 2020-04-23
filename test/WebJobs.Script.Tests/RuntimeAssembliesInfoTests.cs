// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Azure.WebJobs.Script.Description;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class RuntimeAssembliesInfoTests
    {
        [Fact]
        public void Reset_WithUnloadedAssemblies_ReturnsFalse()
        {
            var runtimeAssembliesInfo = new RuntimeAssembliesInfo();

            bool result = runtimeAssembliesInfo.ResetIfStale();

            Assert.False(result);
        }

        [Fact]
        public void Reset_WithLoadedAssemblies_AndMatchingCompatMode_ReturnsFalse()
        {
            var environment = new TestEnvironment();
            var runtimeAssembliesInfo = new RuntimeAssembliesInfo(environment);

            // Cause a load
            var assemblies = runtimeAssembliesInfo.Assemblies;

            bool result = runtimeAssembliesInfo.ResetIfStale();

            Assert.NotNull(assemblies);
            Assert.False(result);
        }

        [Fact]
        public void Reset_WithLoadedAssemblies_ChangedRules_ReturnsTrue()
        {
            var environment = new TestEnvironment();
            var runtimeAssembliesInfo = new RuntimeAssembliesInfo(environment);

            // Cause a load
            var originalAssemblies = runtimeAssembliesInfo.Assemblies;

            // Change environment
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagRelaxedAssemblyUnification);

            bool result = runtimeAssembliesInfo.ResetIfStale();

            var newAssemblies = runtimeAssembliesInfo.Assemblies;

            Assert.NotNull(originalAssemblies);
            Assert.NotNull(newAssemblies);
            Assert.NotSame(originalAssemblies, newAssemblies);
            Assert.True(result);
        }

        [Fact]
        public void Load_WithRelaxedRules_ReturnsExpectedResult()
        {
            var environment = new TestEnvironment();
            var runtimeAssembliesInfo = new RuntimeAssembliesInfo(environment);

            // Change environment
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagRelaxedAssemblyUnification);

            // Cause a load
            var assemblies = runtimeAssembliesInfo.Assemblies;

            assemblies.TryGetValue("Newtonsoft.Json", out ScriptRuntimeAssembly assembly);

            Assert.NotNull(assemblies);
            Assert.Null(assembly);
        }
    }
}
