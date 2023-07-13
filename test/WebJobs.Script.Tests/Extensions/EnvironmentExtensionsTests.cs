// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.WebJobs.Script.Tests;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class EnvironmentExtensionsTests
    {
        [Fact]
        public void GetEffectiveCoresCount_ReturnsExpectedResult()
        {
            TestEnvironment env = new TestEnvironment();
            Assert.Equal(Environment.ProcessorCount, EnvironmentExtensions.GetEffectiveCoresCount(env));

            env.Clear();
            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.DynamicSku);
            Assert.Equal(1, EnvironmentExtensions.GetEffectiveCoresCount(env));

            env.Clear();
            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.DynamicSku);
            env.SetEnvironmentVariable(EnvironmentSettingNames.RoleInstanceId, "dw0SmallDedicatedWebWorkerRole_hr0HostRole-0-VM-1");
            Assert.Equal(Environment.ProcessorCount, EnvironmentExtensions.GetEffectiveCoresCount(env));
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.AdminIsolationTests)]
        public void IsAdminIsolationEnabled_ReturnsExpectedResult()
        {
            TestEnvironment env = new TestEnvironment();
            Assert.False(EnvironmentExtensions.IsAdminIsolationEnabled(env));

            env.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsAdminIsolationEnabled, "0");
            Assert.False(EnvironmentExtensions.IsAdminIsolationEnabled(env));

            env.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsAdminIsolationEnabled, "1");
            Assert.True(EnvironmentExtensions.IsAdminIsolationEnabled(env));
        }

        [Theory]
        [InlineData("dw0SmallDedicatedWebWorkerRole_hr0HostRole-0-VM-1", true)]
        [InlineData(null, false)]
        [InlineData("", false)]
        public void IsVMSS_RetrunsExpectedResult(string roleInstanceId, bool expected)
        {
            IEnvironment env = new TestEnvironment();
            if (roleInstanceId != null)
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.RoleInstanceId, roleInstanceId);
            }

            Assert.Equal(expected, EnvironmentExtensions.IsVMSS(env));
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData("", false)]
        [InlineData("Foo,FunctionAppLogs,Bar", true)]
        [InlineData("Foo,Bar", false)]
        public void IsAzureMonitorEnabled_ReturnsExpectedResult(string value, bool expected)
        {
            IEnvironment env = new TestEnvironment();
            if (value != null)
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.AzureMonitorCategories, value);
            }

            Assert.Equal(expected, EnvironmentExtensions.IsAzureMonitorEnabled(env));
        }

        [Theory]
        [InlineData("RD281878FCB8E7", "RD281878FCB8E7")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void GetAntaresComputerName_ReturnsExpectedResult(string computerName, string expectedComputerName)
        {
            IEnvironment env = new TestEnvironment();
            if (!string.IsNullOrEmpty(expectedComputerName))
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.AntaresComputerName, computerName);
            }

            Assert.Equal(expectedComputerName, env.GetAntaresComputerName());
        }

        [Theory]
        [InlineData(true, "RandomContainerName", "", "RandomContainerName")]
        [InlineData(true, "RandomContainerName", "e777fde04dea4eb931d5e5f06e65b4fdf5b375aed60af41dd7b491cf5792e01b", "RandomContainerName")]
        [InlineData(true, "", "", "")]
        [InlineData(true, null, "", "")]
        [InlineData(false, "RandomContainerName", "", "")]
        [InlineData(false, "RandomContainerName", "e777fde04dea4eb931d5e5f06e65b4fdf5b375aed60af41dd7b491cf5792e01b", "e777fde04dea4eb931d5e5f06e65b4fdf5b375aed60af41dd7b491cf5792e01b")]
        [InlineData(false, "", "", "")]
        [InlineData(false, "", null, "")]
        public void GetInstanceId_ReturnsExpectedResult(bool isLinuxConsumption, string containerName, string websiteInstanceId, string expectedValue)
        {
            IEnvironment env = new TestEnvironment();
            if (isLinuxConsumption)
            {
                if (!string.IsNullOrEmpty(containerName))
                {
                    env.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, containerName);
                }
            }
            else if (!string.IsNullOrEmpty(websiteInstanceId))
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, websiteInstanceId);
            }

            Assert.Equal(expectedValue, env.GetInstanceId());
        }

        [Theory]
        [InlineData(true, false, "89.0.7.73", "89.0.7.74", "89.0.7.73")]
        [InlineData(true, false, "", "89.0.7.74", "")]
        [InlineData(true, false, null, "89.0.7.74", "")]
        [InlineData(true, true, "89.0.7.73", "89.0.7.74", "89.0.7.73")]
        [InlineData(true, true, "", "89.0.7.74", "")]
        [InlineData(true, true, null, "89.0.7.74", "")]
        [InlineData(false, true, "89.0.7.73", "89.0.7.74", "89.0.7.73")]
        [InlineData(false, true, "", "89.0.7.74", "")]
        [InlineData(false, true, null, "89.0.7.74", "")]
        [InlineData(false, false, "89.0.7.73", "89.0.7.74", "89.0.7.74")]
        [InlineData(false, false, "89.0.7.73", "", "")]
        [InlineData(false, false, "89.0.7.73", null, "")]
        public void GetAntaresVersion_ReturnsExpectedResult(bool isLinuxConsumption, bool isLinuxAppService, string platformVersionLinux, string platformVersionWindows, string expectedValue)
        {
            IEnvironment env = new TestEnvironment();
            if (isLinuxConsumption)
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "RandomContainerName");
            }

            if (isLinuxAppService)
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, "e777fde04dea4eb931d5e5f06e65b4fdf5b375aed60af41dd7b491cf5792e01b");
                env.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsLogsMountPath, "C:\\SomeMountPath");
            }

            if (!string.IsNullOrEmpty(platformVersionLinux))
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformVersionLinux, platformVersionLinux);
            }

            if (!string.IsNullOrEmpty(platformVersionWindows))
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformVersionWindows, platformVersionWindows);
            }

            Assert.Equal(expectedValue, env.GetAntaresVersion());
        }

        [Theory]
        [InlineData(true, false, false, true)]
        [InlineData(false, false, true, true)]
        [InlineData(false, true, false, true)]
        [InlineData(false, false, false, false)]
        [InlineData(true, true, false, true)]
        public void IsConsumptionSku_ReturnsExpectedResult(bool isLinuxConsumption, bool isWindowsConsumption, bool isFlexConsumption, bool expectedValue)
        {
            IEnvironment env = new TestEnvironment();
            if (isLinuxConsumption)
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "RandomContainerName");
            }

            if (isWindowsConsumption)
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.DynamicSku);
            }

            if (isFlexConsumption)
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.FlexConsumptionSku);
            }

            Assert.Equal(expectedValue, env.IsConsumptionSku());
        }

        [Theory]
        [InlineData("FlexConsumption", "", "", "", true)] // not a valid configuration, but testing for thoroughness
        [InlineData(null, "", "container-name", "1", true)] // simulate placeholder mode where SKU not available yet
        [InlineData("FlexConsumption", "", "container-name", "1", true)] // expected state when specialized
        [InlineData(null, "website-instance-id", "container-name", "1", false)] // not a valid configuration, but testing for thoroughness
        [InlineData("Dynamic", "", "", "", false)]
        [InlineData("ElasticPremium", "", "", "", false)]
        [InlineData("", "", "", "", false)]
        public void IsFlexConsumptionSku_ReturnsExpectedResult(string sku, string websiteInstanceId, string containerName, string legionServiceHost, bool expected)
        {
            IEnvironment env = new TestEnvironment();
            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, sku);
            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, websiteInstanceId);
            env.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, containerName);
            env.SetEnvironmentVariable(EnvironmentSettingNames.LegionServiceHost, legionServiceHost);

            Assert.Equal(expected, env.IsFlexConsumptionSku());
        }

        [Theory]
        [InlineData(true, false, false, true, false)]
        [InlineData(false, true, false, true, false)]
        [InlineData(false, true, false, true, true)]
        [InlineData(true, true, false, true, false)]
        [InlineData(true, true, false, true, true)]
        [InlineData(false, false, false, false, false)]
        [InlineData(false, false, true, false, false)]
        public void IsAnyLinuxConsumption_ReturnsExpectedResult(bool isLinuxConsumptionOnAtlas, bool isLinuxConsumptionOnLegion, bool isManagedAppEnvironment, bool expectedValue, bool setPodName)
        {
            IEnvironment env = new TestEnvironment();
            if (isLinuxConsumptionOnAtlas)
            {
                env.SetEnvironmentVariable(ContainerName, "RandomContainerName");
            }

            if (isLinuxConsumptionOnLegion)
            {
                if (setPodName)
                {
                    env.SetEnvironmentVariable(WebsitePodName, "RandomPodName");
                }
                else
                {
                    env.SetEnvironmentVariable(ContainerName, "RandomContainerName");
                }
                env.SetEnvironmentVariable(LegionServiceHost, "RandomLegionServiceHostName");
            }

            if (isManagedAppEnvironment)
            {
                env.SetEnvironmentVariable(ManagedEnvironment, "true");
            }

            Assert.Equal(expectedValue, env.IsAnyLinuxConsumption());
        }

        [Theory]
        [InlineData(true, false, true)]
        [InlineData(false, true, true)]
        [InlineData(false, false, false)]
        public void IsAnyKubernetesEnvironment_ReturnsExpectedResult(bool isKubernetesManagedHosting, bool isManagedAppEnvironment, bool expectedValue)
        {
            IEnvironment env = new TestEnvironment();
            if (isKubernetesManagedHosting)
            {
                env.SetEnvironmentVariable(KubernetesServiceHost, "10.0.0.1");
                env.SetEnvironmentVariable(PodNamespace, "RandomPodNamespace");
            }

            if (isManagedAppEnvironment)
            {
                env.SetEnvironmentVariable(ManagedEnvironment, "true");
            }

            Assert.Equal(expectedValue, env.IsAnyKubernetesEnvironment());
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public void IsManagedAppEnvironment_ReturnsExpectedResult(bool isManagedAppEnvironment, bool expectedValue)
        {
            IEnvironment env = new TestEnvironment();
            if (isManagedAppEnvironment)
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.ManagedEnvironment, "true");
            }

            Assert.Equal(expectedValue, env.IsManagedAppEnvironment());
        }

        [Theory]
        [InlineData("~2", "true", true)]
        [InlineData("~2", "false", true)]
        [InlineData("~2", null, true)]
        [InlineData("~3", "true", true)]
        [InlineData("~3", "false", false)]
        [InlineData("~3", null, false)]
        [InlineData(null, null, false)]
        public void IsV2CompatMode(string extensionVersion, string compatMode, bool expected)
        {
            IEnvironment env = new TestEnvironment();

            if (extensionVersion != null)
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsExtensionVersion, extensionVersion);
            }

            if (compatMode != null)
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsV2CompatibilityModeKey, compatMode);
            }

            Assert.Equal(expected, EnvironmentExtensions.IsV2CompatibilityMode(env));
        }

        [Theory]
        [InlineData("k8se-apps-ns", "10.0.0.1", false, true)]
        [InlineData("k8se-apps", null, false, false)]
        [InlineData(null, "10.0.0.1", false, false)]
        [InlineData(null, null, true, false)]
        [InlineData("k8se-apps", "10.0.0.1", true, false)]
        public void IsKubernetesManagedHosting_ReturnsExpectedResult(string podNamespace, string kubernetesServiceHost, bool isManagedAppEnvironment, bool expected)
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(KubernetesServiceHost, kubernetesServiceHost);
            environment.SetEnvironmentVariable(PodNamespace, podNamespace);
            if (isManagedAppEnvironment)
            {
                environment.SetEnvironmentVariable(ManagedEnvironment, "true");
            }

            Assert.Equal(expected, environment.IsKubernetesManagedHosting());
        }

        [Theory]
        [InlineData(RpcWorkerConstants.PowerShellLanguageWorkerName, RpcWorkerConstants.PowerShellLanguageWorkerName)]
        [InlineData(RpcWorkerConstants.DotNetLanguageWorkerName, RpcWorkerConstants.DotNetLanguageWorkerName)]
        [InlineData(RpcWorkerConstants.PythonLanguageWorkerName, RpcWorkerConstants.PythonLanguageWorkerName)]
        [InlineData(RpcWorkerConstants.JavaLanguageWorkerName, RpcWorkerConstants.JavaLanguageWorkerName)]
        [InlineData(RpcWorkerConstants.NodeLanguageWorkerName, RpcWorkerConstants.NodeLanguageWorkerName)]
        [InlineData(null, "")]
        [InlineData("", "")]
        public void Returns_WorkerRuntime(string workerRuntime, string expectedWorkerRuntime)
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(FunctionWorkerRuntime, workerRuntime);
            Assert.Equal(expectedWorkerRuntime, environment.GetFunctionsWorkerRuntime());
        }

        [Theory]
        [InlineData("~1", "~1")]
        [InlineData("~2", "~2")]
        [InlineData("~3", "~3")]
        [InlineData("~4", "~4")]
        [InlineData(null, "")]
        [InlineData("", "")]
        public void Returns_FunctionsExtensionVersion(string functionsExtensionVersion, string functionsExtensionVersionExpected)
        {
            var enviroment = new TestEnvironment();
            enviroment.SetEnvironmentVariable(FunctionsExtensionVersion, functionsExtensionVersion);
            Assert.Equal(functionsExtensionVersionExpected, enviroment.GetFunctionsExtensionVersion());
        }

        [Theory]
        [InlineData(RpcWorkerConstants.PowerShellLanguageWorkerName, true, false, true)]
        [InlineData(RpcWorkerConstants.PowerShellLanguageWorkerName, false, true, true)]
        [InlineData(RpcWorkerConstants.PowerShellLanguageWorkerName, false, false, true)]
        [InlineData(RpcWorkerConstants.DotNetLanguageWorkerName, true, false, false)]
        [InlineData(RpcWorkerConstants.DotNetLanguageWorkerName, false, true, false)]
        [InlineData(RpcWorkerConstants.DotNetLanguageWorkerName, false, false, false)]
        [InlineData(RpcWorkerConstants.PythonLanguageWorkerName, false, true, false)]
        [InlineData(RpcWorkerConstants.JavaLanguageWorkerName, false, false, false)]
        [InlineData(RpcWorkerConstants.NodeLanguageWorkerName, true, false, false)]
        [InlineData(null, false, false, false)]
        [InlineData("", false, false, false)]
        public void Returns_SupportsAzureFileShareMount(string workerRuntime, bool useLowerCase, bool useUpperCase, bool supportsAzureFileShareMount)
        {
            var environment = new TestEnvironment();
            if (useLowerCase && !useUpperCase)
            {
                workerRuntime = workerRuntime.ToLowerInvariant();
            }

            if (useUpperCase && !useLowerCase)
            {
                workerRuntime = workerRuntime.ToUpperInvariant();
            }
            environment.SetEnvironmentVariable(FunctionWorkerRuntime, workerRuntime);
            Assert.Equal(supportsAzureFileShareMount, environment.SupportsAzureFileShareMount());
        }

        [Theory]
        [InlineData("test-endpoint", "test-endpoint")]
        [InlineData(null, "")]
        [InlineData("", "")]
        public void Returns_GetHttpLeaderEndpoint(string httpLeaderEndpoint, string expected)
        {
            var environment = new TestEnvironment();

            if (!string.IsNullOrEmpty(httpLeaderEndpoint))
            {
                environment.SetEnvironmentVariable(HttpLeaderEndpoint, httpLeaderEndpoint);
            }
            Assert.Equal(expected, environment.GetHttpLeaderEndpoint());
        }

        [Theory]
        [InlineData(null, null, false)]
        [InlineData("", null, false)]
        [InlineData("", "", false)]
        [InlineData("", "false", false)]
        [InlineData("", "true", true)]
        [InlineData("10.0.0.1", "", true)]
        [InlineData("10.0.0.1", "true", true)]
        [InlineData("10.0.0.1", "false", true)]
        [InlineData("10.0.0.1", null, true)]
        public void IsDrainOnApplicationStopping_ReturnsExpectedResult(string serviceHostValue, string drainOnStoppingValue, bool expected)
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(KubernetesServiceHost, serviceHostValue);
            environment.SetEnvironmentVariable(DrainOnApplicationStopping, drainOnStoppingValue);
            Assert.Equal(expected, environment.DrainOnApplicationStoppingEnabled());
        }

        [Theory]
        [InlineData(null, null, false)]
        [InlineData("true", "1", false)]
        [InlineData("true", "100", false)]
        [InlineData("true", null, true)]
        public void IsWorkerDynamicConcurrencyEnabled_ReturnsExpectedResult(string concurrencyEnabledValue, string processCountValue, bool expected)
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled, concurrencyEnabledValue);
            environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerProcessCountSettingName, processCountValue);
            Assert.Equal(expected, environment.IsWorkerDynamicConcurrencyEnabled());
        }

        [Theory]
        [InlineData(null, null, "")]
        [InlineData("", "", "")]
        [InlineData("node", "python;java", "node;python;java")]
        [InlineData("", "node;python", "node;python")]
        [InlineData("", "python;java;", "python;java")]
        [InlineData("node", "", "node")]
        public void GetLanguageWorkerListToStartInPlaceholder_ReturnsExpectedResult(string workerRuntime, string workerRuntimeList, string expected)
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);
            environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerPlaceholderModeListSettingName, workerRuntimeList);
            var resultSet = environment.GetLanguageWorkerListToStartInPlaceholder();
            if (string.IsNullOrEmpty(expected))
            {
                Assert.Empty(resultSet);
                return;
            }
            var expectedSet = new HashSet<string>(expected.Split(';'));
            Assert.True(resultSet.SetEquals(expectedSet));
        }

        [Theory]
        [InlineData(null, null, false)]
        [InlineData(null, "test", false)]
        [InlineData("test", null, false)]
        [InlineData("test", "test", true)]
        public void AzureFilesAppSettingsExist_ReturnsExpectedResult(string connectionString, string contentShare, bool expected)
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(AzureFilesConnectionString, connectionString);
            environment.SetEnvironmentVariable(AzureFilesContentShare, contentShare);
            Assert.Equal(expected, environment.AzureFilesAppSettingsExist());
        }
    }
}
