// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.Storage;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.ExtensionRequirements;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json.Linq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public sealed class ScriptStartupTypeDiscovererTests : IDisposable
    {
        private readonly TempDirectory _directory = new();
        private readonly TestMetricsLogger _metricsLogger = new();
        private readonly TestLoggerProvider _loggerProvider = new();
        private readonly TestEnvironment _environment = new();
        private readonly Mock<IExtensionBundleManager> _bundleManager = new();
        private readonly Mock<IFunctionMetadataManager> _metadataManager = new();

        public ScriptStartupTypeDiscovererTests()
        {
            SetupMetadataManager(null);
        }

        public void Dispose()
        {
            _directory.Dispose();
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_UsesDefaultMinVersion()
        {
            InstallExtensions(ExtensionInstall.BlobStorage(true), ExtensionInstall.QueueStorage(true));
            var binPath = Path.Combine(_directory.Path, "bin");

            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).ReturnsAsync(binPath);
            _bundleManager.Setup(e => e.IsLegacyExtensionBundle()).Returns(false);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails("2.1.0"));

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            var exception = await Assert.ThrowsAsync<HostInitializationException>(discoverer.GetExtensionsStartupTypesAsync);
            var traces = _loggerProvider.GetAllLogMessages();

            // Assert
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"Referenced bundle Microsoft.Azure.Functions.ExtensionBundle of version 2.1.0 does not meet the required minimum version of 2.6.1. Update your extension bundle reference in host.json to reference 2.6.1 or later.")));
        }

        [Theory]
        [InlineData("4.12.0", "4.9.0")]
        [InlineData("2.6.1", "2.1.0")]
        public async Task GetExtensionsStartupTypes_RejectsBundleConfiguredviaHostingEnvConfig(string expectedBundleVersion, string actualBundleVersion)
        {
            var binPath = Path.Combine(_directory.Path, "bin");

            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).ReturnsAsync(binPath);
            _bundleManager.Setup(e => e.IsLegacyExtensionBundle()).Returns(false);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails(actualBundleVersion));

            ExtensionRequirementOptions extensionRequirementOptions = new()
            {
                Bundles =
                [
                    new()
                    {
                        Id = "Microsoft.Azure.Functions.ExtensionBundle",
                        MinimumVersion = expectedBundleVersion
                    }
                ]
            };

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest(extensionRequirements: extensionRequirementOptions);
            var exception = await Assert.ThrowsAsync<HostInitializationException>(async () => await discoverer.GetExtensionsStartupTypesAsync());
            var traces = _loggerProvider.GetAllLogMessages();

            // Assert
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"Referenced bundle Microsoft.Azure.Functions.ExtensionBundle of version {actualBundleVersion} does not meet the required minimum version of {expectedBundleVersion}. Update your extension bundle reference in host.json to reference {expectedBundleVersion} or later.")));
        }

        [Theory]
        [InlineData("4.12.0", "4.17.0", null)]
        [InlineData("4.12.0", null, "4.0.4")]
        [InlineData("4.12.0", "4.17.0", "4.0.4")]
        [InlineData(null, "4.17.0", "4.0.4")]
        public async Task GetExtensionsStartupTypes_AcceptsRequiredBundleVersions(string minBundleVersion, string actualBundleVersion, string minExtensionVersion)
        {
            string binPath = InstallExtensions(ExtensionInstall.BlobStorage(true), ExtensionInstall.QueueStorage(true));

            if (string.IsNullOrEmpty(actualBundleVersion))
            {
                _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(false);
            }
            else
            {
                _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            }

            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).ReturnsAsync(binPath);
            _bundleManager.Setup(e => e.IsLegacyExtensionBundle()).Returns(false);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails(actualBundleVersion));

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest(extensionRequirements: GetExtensionRequirementOptions(minBundleVersion, minExtensionVersion));
            var types = await discoverer.GetExtensionsStartupTypesAsync();
            var traces = _loggerProvider.GetAllLogMessages();

            // Assert

            if (string.IsNullOrEmpty(actualBundleVersion))
            {
                Assert.True(traces.Any(m => m.FormattedMessage.Contains($"Extension Bundle not loaded")));
            }
            else
            {
                Assert.True(traces.Any(m => m.FormattedMessage.Contains($"Loading extension bundle")));
            }

            Assert.True(traces.Any(m => m.FormattedMessage.Contains($"Loading startup extension 'AzureStorageBlobs'")));
            AssertNoErrors(traces);
        }

        [Theory]
        [InlineData("4.12.0", "4.9.0", null)]
        [InlineData(null, "4.9.0", "5.4.0")]
        [InlineData("4.12.0", "4.9.0", "5.4.0")]
        public async Task GetExtensionsStartupTypes_RejectsRequiredBundleVersions(string minBundleVersion, string actualBundleVersion, string minExtensionVersion)
        {
            string binPath = InstallExtensions(ExtensionInstall.BlobStorage(true), ExtensionInstall.QueueStorage(true));
            if (string.IsNullOrEmpty(actualBundleVersion))
            {
                _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(false);
            }
            else
            {
                _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            }

            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).ReturnsAsync(binPath);
            _bundleManager.Setup(e => e.IsLegacyExtensionBundle()).Returns(false);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails(actualBundleVersion));

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest(extensionRequirements: GetExtensionRequirementOptions(minBundleVersion, minExtensionVersion));
            var exception = await Assert.ThrowsAsync<HostInitializationException>(discoverer.GetExtensionsStartupTypesAsync);
            var traces = _loggerProvider.GetAllLogMessages();

            // Assert
            if (!string.IsNullOrEmpty(minBundleVersion))
            {
                Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"Referenced bundle Microsoft.Azure.Functions.ExtensionBundle of version 4.9.0 does not meet the required minimum version of 4.12.0. Update your extension bundle reference in host.json to reference 4.12.0 or later.")));
            }
        }

        [Theory]
        [InlineData("4.12.0", true, null)]
        [InlineData(null, false, "4.0.4")]
        [InlineData("4.12.0", true, "4.0.4")]
        [InlineData("4.12.0", false, "4.0.4")]
        public async Task GetExtensionsStartupTypes_AcceptsRequiredExtensionVersions(string minBundleVersion, bool extensionConfigured, string minExtensionVersion)
        {
            if (extensionConfigured)
            {
                InstallExtensions(ExtensionInstall.BlobStorage(true), ExtensionInstall.QueueStorage(true));
            }

            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(false);

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest(extensionRequirements: GetExtensionRequirementOptions(minBundleVersion, minExtensionVersion));
            var types = await discoverer.GetExtensionsStartupTypesAsync();
            var traces = _loggerProvider.GetAllLogMessages();

            // Assert
            Assert.True(traces.Any(m => m.FormattedMessage.Contains($"Extension Bundle not loaded")));
            if (extensionConfigured)
            {
                Assert.True(traces.Any(m => m.FormattedMessage.Contains($"Loading startup extension 'AzureStorageBlobs'")));
            }

            AssertNoErrors(traces);
        }

        [Theory]
        [InlineData(null, "5.4.0")]
        [InlineData("4.12.0", "5.4.0")]
        public async Task GetExtensionsStartupTypes_RejectsRequiredExtensionVersions(string minBundleVersion, string minExtensionVersion)
        {
            string binPath = InstallExtensions(ExtensionInstall.BlobStorage(true), ExtensionInstall.QueueStorage(true));
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(false);

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest(extensionRequirements: GetExtensionRequirementOptions(minBundleVersion, minExtensionVersion));
            var exception = await Assert.ThrowsAsync<HostInitializationException>(discoverer.GetExtensionsStartupTypesAsync);

            // Assert
            var traces = _loggerProvider.GetAllLogMessages();
            Assert.True(traces.Any(m => m.FormattedMessage.Contains($"Extension Bundle not loaded")));
            Assert.True(traces.Any(m => m.FormattedMessage.Contains($"ExtensionStartupType AzureStorageBlobsWebJobsStartup from assembly 'Microsoft.Azure.WebJobs.Extensions.Storage.Blobs, Version=5.3.0.0, Culture=neutral, PublicKeyToken=92742159e12e44c8' does not meet the required minimum version of 5.4.0.")));
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_FiltersBuiltinExtensionsAsync()
        {
            InstallExtensions(ExtensionInstall.Http(), ExtensionInstall.Timers(), ExtensionInstall.BlobStorage());
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(false);

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            var types = await discoverer.GetExtensionsStartupTypesAsync();
            var traces = _loggerProvider.GetAllLogMessages();

            // Assert
            AreExpectedMetricsGenerated();
            Assert.Single(types);
            Assert.Equal(typeof(AzureStorageBlobsWebJobsStartup).FullName, types.Single().FullName);
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"The extension startup type '{typeof(HttpWebJobsStartup).AssemblyQualifiedName}' belongs to a builtin extension")));
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"The extension startup type '{typeof(ExtensionsWebJobsStartup).AssemblyQualifiedName}' belongs to a builtin extension")));
            AssertNoErrors(traces);
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_ExtensionBundleReturnsNullPath_ReturnsNull()
        {
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundlePath()).ReturnsAsync(string.Empty);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails());

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            var types = await discoverer.GetExtensionsStartupTypesAsync();
            var traces = _loggerProvider.GetAllLogMessages();

            // Assert
            AreExpectedMetricsGenerated();
            Assert.NotNull(types);
            Assert.Equal(types.Count(), 0);
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"Unable to find or download extension bundle")));
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_ValidExtensionBundle_FiltersBuiltinExtensionsAsync()
        {
            string binPath = InstallExtensions(ExtensionInstall.Http(), ExtensionInstall.Timers(), ExtensionInstall.BlobStorage());
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails());
            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).ReturnsAsync(binPath);

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            var types = await discoverer.GetExtensionsStartupTypesAsync();
            var traces = _loggerProvider.GetAllLogMessages();

            // Assert
            Assert.Single(types);
            AreExpectedMetricsGenerated();
            Assert.Equal(typeof(AzureStorageBlobsWebJobsStartup).FullName, types.Single().FullName);
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"The extension startup type '{typeof(HttpWebJobsStartup).AssemblyQualifiedName}' belongs to a builtin extension")));
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"The extension startup type '{typeof(ExtensionsWebJobsStartup).AssemblyQualifiedName}' belongs to a builtin extension")));
            AssertNoErrors(traces);
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_UnableToDownloadExtensionBundle_ReturnsNull()
        {
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails());
            _bundleManager.Setup(e => e.GetExtensionBundlePath()).ReturnsAsync(string.Empty);

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest(string.Empty);
            var types = await discoverer.GetExtensionsStartupTypesAsync();
            var traces = _loggerProvider.GetAllLogMessages();

            // Assert
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"Unable to find or download extension bundle")));
            AreExpectedMetricsGenerated();
            Assert.NotNull(types);
            Assert.Equal(types.Count(), 0);
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_BundlesConfiguredBindingsNotConfigured_LoadsAllExtensions()
        {
            string binPath = InstallExtensions(ExtensionInstall.BlobStorage(), ExtensionInstall.QueueStorage());
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).ReturnsAsync(binPath);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails());

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            var types = await discoverer.GetExtensionsStartupTypesAsync();
            var traces = _loggerProvider.GetAllLogMessages();

            // Assert
            AreExpectedMetricsGenerated();
            Assert.Equal(2, types.Count());
            Assert.Equal(typeof(AzureStorageBlobsWebJobsStartup).FullName, types.FirstOrDefault().FullName);
            AssertNoErrors(traces);
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_BundlesNotConfiguredBindingsNotConfigured_LoadsAllExtensions()
        {
            InstallExtensions(ExtensionInstall.BlobStorage());
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(false);

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            var types = await discoverer.GetExtensionsStartupTypesAsync();
            var traces = _loggerProvider.GetAllLogMessages();

            // Assert
            AreExpectedMetricsGenerated();
            Assert.Single(types);
            Assert.Equal(typeof(AzureStorageBlobsWebJobsStartup).FullName, types.Single().FullName);
            AssertNoErrors(traces);
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_BundlesConfiguredBindingsConfigured_PerformsSelectiveLoading()
        {
            InstallExtensions(ExtensionInstall.BlobStorage(true), ExtensionInstall.QueueStorage(true));
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).ReturnsAsync(Path.Combine(_directory.Path, "bin"));
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails());

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            var types = await discoverer.GetExtensionsStartupTypesAsync();
            var traces = _loggerProvider.GetAllLogMessages();

            //Assert
            AreExpectedMetricsGenerated();
            Assert.Single(types);
            Assert.Equal(typeof(AzureStorageBlobsWebJobsStartup).FullName, types.Single().FullName);
            AssertNoErrors(traces);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetExtensionsStartupTypes_LegacyBundles_UsesExtensionBundleBinaries(bool hasPrecompiledFunctions)
        {
            string binPath = InstallExtensions(ExtensionInstall.BlobStorage(true), ExtensionInstall.QueueStorage(true));
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).ReturnsAsync(binPath);
            _bundleManager.Setup(e => e.IsLegacyExtensionBundle()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails());

            if (hasPrecompiledFunctions)
            {
                SetupMetadataManager(DotNetScriptTypes.DotNetAssembly);
            }

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            var types = await discoverer.GetExtensionsStartupTypesAsync();

            //Assert
            AreExpectedMetricsGenerated();
            Assert.Single(types);
            Assert.Equal(typeof(AzureStorageBlobsWebJobsStartup).FullName, types.Single().FullName);
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_WorkerRuntimeNotSetForNodeApp_LoadsExtensionBundle()
        {
            string binPath = InstallExtensions(ExtensionInstall.BlobStorage(true), ExtensionInstall.QueueStorage(true));
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).ReturnsAsync(binPath);
            _bundleManager.Setup(e => e.IsLegacyExtensionBundle()).Returns(false);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails());
            SetupMetadataManager(RpcWorkerConstants.NodeLanguageWorkerName);

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            var types = await discoverer.GetExtensionsStartupTypesAsync();

            //Assert
            var traces = _loggerProvider.GetAllLogMessages();
            var traceMessage = traces.FirstOrDefault(val => val.EventId.Name.Equals("ScriptStartNotLoadingExtensionBundle"));
            bool loadingExtensionBundle = traceMessage == null;

            Assert.True(loadingExtensionBundle);
            AreExpectedMetricsGenerated();
            Assert.Single(types);
            Assert.Equal(typeof(AzureStorageBlobsWebJobsStartup).FullName, types.Single().FullName);
        }

        [Theory(Skip = "This test is failing on CI and needs to be fixed.")]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public async Task GetExtensionsStartupTypes_DotnetIsolated_ExtensionBundleConfigured(bool isLogicApp, bool workerRuntimeSet)
        {
            if (isLogicApp)
            {
                _environment.SetEnvironmentVariable(EnvironmentSettingNames.AppKind, ScriptConstants.WorkFlowAppKind);
            }

            if (workerRuntimeSet)
            {
                _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "dotnet-isolated");
            }

            var binPath = Path.Combine(_directory.Path, "bin");
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).ReturnsAsync(binPath);
            _bundleManager.Setup(e => e.IsLegacyExtensionBundle()).Returns(false);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails());
            SetupMetadataManager(RpcWorkerConstants.DotNetIsolatedLanguageWorkerName);

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            var types = await discoverer.GetExtensionsStartupTypesAsync();

            //Assert
            var traces = _loggerProvider.GetAllLogMessages();
            var traceMessage = traces.FirstOrDefault(val => val.EventId.Name.Equals("ScriptStartNotLoadingExtensionBundle"));
            bool loadingExtensionBundle = traceMessage == null;

            if (isLogicApp)
            {
                Assert.True(loadingExtensionBundle);
            }
            else
            {
                Assert.False(loadingExtensionBundle);
            }

            AreExpectedMetricsGenerated();
            Assert.Single(types);
            Assert.Equal(typeof(AzureStorageBlobsWebJobsStartup).FullName, types.Single().FullName);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetExtensionsStartupTypes_NonLegacyBundles_UsesBundlesForNonPrecompiledFunctions(bool hasPrecompiledFunctions)
        {
            InstallExtensions(ExtensionInstall.BlobStorage(true), ExtensionInstall.QueueStorage(true));
            string bundlePath = hasPrecompiledFunctions ? "FakePath" : _directory.Path;
            var binPath = Path.Combine(_directory.Path, "bin");

            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).ReturnsAsync(binPath);
            _bundleManager.Setup(e => e.IsLegacyExtensionBundle()).Returns(false);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails());
            SetupMetadataManager(DotNetScriptTypes.DotNetAssembly);

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            var types = await discoverer.GetExtensionsStartupTypesAsync();

            //Assert
            AreExpectedMetricsGenerated();
            Assert.Single(types);
            Assert.Equal(typeof(AzureStorageBlobsWebJobsStartup).FullName, types.Single().FullName);
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_BundlesNotConfiguredBindingsConfigured_LoadsAllExtensions()
        {
            InstallExtensions(ExtensionInstall.BlobStorage(true), ExtensionInstall.QueueStorage(true));
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(false);

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            var types = await discoverer.GetExtensionsStartupTypesAsync();

            // Assert
            AreExpectedMetricsGenerated();
            Assert.Equal(types.Count(), 2);
            Assert.Equal(typeof(AzureStorageBlobsWebJobsStartup).FullName, types.FirstOrDefault().FullName);
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_NoBindings_In_ExtensionJson()
        {
            ExtensionInstall storage1 = new("AzureStorageBlobs", typeof(AzureStorageBlobsWebJobsStartup));
            ExtensionInstall storage2 = new("AzureStorageQueues", typeof(AzureStorageQueuesWebJobsStartup));

            string binPath = InstallExtensions(storage1, storage2);
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails());
            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).ReturnsAsync(binPath);

            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            var types = await discoverer.GetExtensionsStartupTypesAsync();
            var traces = _loggerProvider.GetAllLogMessages();

            AreExpectedMetricsGenerated();
            Assert.Equal(2, types.Count());
            Assert.Contains(types, t => t.FullName == typeof(AzureStorageBlobsWebJobsStartup).FullName);
            AssertNoErrors(traces);
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_BundleConfigured_NullBindingsInJson_LoadsExtensionWithoutThrowing()
        {
            // Regression guard for the selective-loading short-circuit in
            // ScriptStartupTypeLocator.GetExtensionsStartupTypesAsync. When an extension's
            // "bindings" property is explicitly null in extensions.json, the null/empty check
            // (shouldLoadAllExtensions) must short-circuit before extensionItem.Bindings.Intersect(...)
            // is evaluated, otherwise a NullReferenceException is thrown.
            string binPath = Path.Combine(_directory.Path, "bin");
            Directory.CreateDirectory(binPath);

            string assemblyPath = typeof(AzureStorageBlobsWebJobsStartup).Assembly.Location;
            File.Copy(assemblyPath, Path.Combine(binPath, Path.GetFileName(assemblyPath)), overwrite: true);

            string extensionsJson = $$"""
            {
              "extensions": [
                {
                  "name": "AzureStorageBlobs",
                  "typeName": "{{typeof(AzureStorageBlobsWebJobsStartup).AssemblyQualifiedName}}",
                  "bindings": null
                }
              ]
            }
            """;
            File.WriteAllText(Path.Combine(binPath, "extensions.json"), extensionsJson);

            // Bundle configured + a function with a "blob" binding -> bindingsSet is non-null,
            // which is the precondition that exposes the bug if the short-circuit is removed.
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).ReturnsAsync(binPath);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails());

            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            var types = await discoverer.GetExtensionsStartupTypesAsync();
            var traces = _loggerProvider.GetAllLogMessages();

            AreExpectedMetricsGenerated();
            Assert.Single(types);
            Assert.Equal(typeof(AzureStorageBlobsWebJobsStartup).FullName, types.Single().FullName);
            AssertNoErrors(traces);
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_RejectsBundleBelowMinimumVersion()
        {
            var binPath = Path.Combine(_directory.Path, "bin");

            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).ReturnsAsync(binPath);
            _bundleManager.Setup(e => e.IsLegacyExtensionBundle()).Returns(false);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails("2.1.0"));

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            var exception = await Assert.ThrowsAsync<HostInitializationException>(discoverer.GetExtensionsStartupTypesAsync);
            var traces = _loggerProvider.GetAllLogMessages();

            // Assert
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"Referenced bundle Microsoft.Azure.Functions.ExtensionBundle of version 2.1.0 does not meet the required minimum version of 2.6.1. Update your extension bundle reference in host.json to reference 2.6.1 or later.")));
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_RejectsExtensionsBelowMinimumVersion()
        {
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(false);

            var binPath = Path.Combine(_directory.Path, "bin");
            Directory.CreateDirectory(binPath);

            void CopyToBin(string path)
            {
                File.Copy(path, Path.Combine(binPath, Path.GetFileName(path)));
            }

            // create a bin folder that has out of date extensions
            var extensionBinPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\OutOfDateExtension\bin");
            foreach (var f in Directory.GetFiles(extensionBinPath))
            {
                CopyToBin(f);
            }

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            var exception = await Assert.ThrowsAsync<HostInitializationException>(discoverer.GetExtensionsStartupTypesAsync);
            var traces = _loggerProvider.GetAllLogMessages();

            // Assert
            var storageTrace = traces.FirstOrDefault(m => m.FormattedMessage.StartsWith("ExtensionStartupType AzureStorageWebJobsStartup"));
            Assert.NotNull(storageTrace);
            Assert.Equal("ExtensionStartupType AzureStorageWebJobsStartup from assembly 'Microsoft.Azure.WebJobs.Extensions.Storage, Version=3.0.10.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35' does not meet the required minimum version of 4.0.4.0. Update your NuGet package reference for Microsoft.Azure.WebJobs.Extensions.Storage to 4.0.4 or later.",
                storageTrace.FormattedMessage);
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_WorkerIndexing_PerformsSelectiveLoading()
        {
            string binPath = InstallExtensions(ExtensionInstall.BlobStorage(true), ExtensionInstall.QueueStorage(true));
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(new ExtensionBundleDetails() { Id = "bundleID", Version = "1.0.0" });
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).ReturnsAsync(binPath);

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "python");

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            var types = await discoverer.GetExtensionsStartupTypesAsync();
            var traces = _loggerProvider.GetAllLogMessages();

            //Assert that filtering did not take place because of worker indexing
            Assert.True(types.Count() == 1);
            Assert.Equal(typeof(AzureStorageBlobsWebJobsStartup).FullName, types.ElementAt(0).FullName);
            AssertNoErrors(traces);
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_EmptyExtensionsArray()
        {
            string binPath = InstallExtensions();
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).Returns(Task.FromResult(new ExtensionBundleDetails() { Id = "bundleID", Version = "1.0.0" }));
            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).Returns(Task.FromResult(binPath));

            // Act
            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            var types = await discoverer.GetExtensionsStartupTypesAsync();
            var traces = _loggerProvider.GetAllLogMessages();

            AreExpectedMetricsGenerated();
            Assert.Empty(types); // Ensure no types are loaded because the extensions array is empty
            AssertNoErrors(traces);
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_BundleConfigured_LogicApp_DoesNotFetchFunctionMetadata()
        {
            // Logic Apps does not use worker indexing and reads function metadata from disk via
            // function.json, so the locator must skip the metadata-fetch on the bundle path.
            // Calling GetFunctionMetadata(forceRefresh: true, ...) here would force a worker process
            // to start before the bundle's IWebJobsConfigurationStartup contributes its
            // languageWorkers:* entries, which is the timing trap PR #11582 introduced.
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AppKind, ScriptConstants.WorkFlowAppKind);

            string binPath = InstallExtensions(ExtensionInstall.BlobStorage(), ExtensionInstall.QueueStorage());
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).ReturnsAsync(binPath);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails());

            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            await discoverer.GetExtensionsStartupTypesAsync();

            _metadataManager.Verify(
                m => m.GetFunctionMetadata(true, It.IsAny<bool>(), It.IsAny<bool>()),
                Times.Never);
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_BundleConfigured_LogicApp_ArmsPostBuildCacheInvalidation()
        {
            // The Logic Apps bundle path must arm post-build invalidation so the worker-options
            // cache is refreshed once the bundle's IWebJobsConfigurationStartup has contributed
            // languageWorkers:<name>:workerDirectory entries to the JobHost IConfiguration.
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AppKind, ScriptConstants.WorkFlowAppKind);

            string binPath = InstallExtensions(ExtensionInstall.BlobStorage(), ExtensionInstall.QueueStorage());
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).ReturnsAsync(binPath);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails());

            var resolverTokenSource = new RefreshWorkerOptionsChangeTokenSource<WorkerConfigurationResolverOptions>();
            var languageWorkerTokenSource = new RefreshWorkerOptionsChangeTokenSource<LanguageWorkerOptions>();
            var invalidator = new WorkerConfigCacheInvalidator(resolverTokenSource, languageWorkerTokenSource);

            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest(workerConfigCacheInvalidator: invalidator);
            await discoverer.GetExtensionsStartupTypesAsync();

            // Snapshot tokens before triggering post-build invalidation so we can detect a refresh.
            var resolverToken = resolverTokenSource.GetChangeToken();
            var languageWorkerToken = languageWorkerTokenSource.GetChangeToken();

            invalidator.InvalidateCachePostBuildIfEnabled();

            Assert.True(resolverToken.HasChanged, "Post-build invalidation was not armed for the Logic Apps bundle path.");
            Assert.True(languageWorkerToken.HasChanged, "Post-build invalidation was not armed for the Logic Apps bundle path.");
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_BundleConfigured_NotLogicApp_FetchesFunctionMetadata()
        {
            // Non-Logic-Apps bundle path is unchanged: the locator still fetches metadata in order
            // to build the bindingsSet that filters which extensions to load.
            string binPath = InstallExtensions(ExtensionInstall.BlobStorage(), ExtensionInstall.QueueStorage());
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).ReturnsAsync(binPath);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails());

            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest();
            await discoverer.GetExtensionsStartupTypesAsync();

            _metadataManager.Verify(
                m => m.GetFunctionMetadata(true, It.IsAny<bool>(), false),
                Times.Once);
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_BundleConfigured_NotLogicApp_DoesNotArmPostBuildCacheInvalidation()
        {
            // The non-Logic-Apps bundle path relies on InvalidateCacheForBundles (a no-op on first
            // run) and must NOT arm post-build invalidation - that path is reserved for the
            // no-bundles branch and the new Logic Apps branch.
            string binPath = InstallExtensions(ExtensionInstall.BlobStorage(), ExtensionInstall.QueueStorage());
            _bundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            _bundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).ReturnsAsync(binPath);
            _bundleManager.Setup(e => e.GetExtensionBundleDetails()).ReturnsAsync(GetBundleDetails());

            var resolverTokenSource = new RefreshWorkerOptionsChangeTokenSource<WorkerConfigurationResolverOptions>();
            var languageWorkerTokenSource = new RefreshWorkerOptionsChangeTokenSource<LanguageWorkerOptions>();
            var invalidator = new WorkerConfigCacheInvalidator(resolverTokenSource, languageWorkerTokenSource);

            ScriptStartupTypeLocator discoverer = CreateSystemUnderTest(workerConfigCacheInvalidator: invalidator);
            await discoverer.GetExtensionsStartupTypesAsync();

            var resolverToken = resolverTokenSource.GetChangeToken();
            var languageWorkerToken = languageWorkerTokenSource.GetChangeToken();

            invalidator.InvalidateCachePostBuildIfEnabled();

            Assert.False(resolverToken.HasChanged);
            Assert.False(languageWorkerToken.HasChanged);
        }

        private static void AssertNoErrors(IList<LogMessage> traces)
        {
            Assert.False(traces.Any(m => m.Level == LogLevel.Error || m.Level == LogLevel.Critical));
        }

        private static ExtensionBundleDetails GetBundleDetails(string version = "2.7.0")
        {
            return new ExtensionBundleDetails
            {
                Id = "Microsoft.Azure.Functions.ExtensionBundle",
                Version = version
            };
        }

        private static ExtensionRequirementOptions GetExtensionRequirementOptions(string minBundleVersion, string minExtensionVersion)
        {
            ExtensionRequirementOptions extensionRequirementOptions = new();
            IEnumerable<BundleRequirement> bundleRequirment = string.IsNullOrEmpty(minBundleVersion)
                ? null
                : [new() { Id = "Microsoft.Azure.Functions.ExtensionBundle", MinimumVersion = minBundleVersion }];

            IEnumerable<ExtensionStartupTypeRequirement> extensionRequirements = string.IsNullOrEmpty(minExtensionVersion)
                ? null :
                [
                    new()
                    {
                        Name = "AzureStorageBlobsWebJobsStartup",
                        AssemblyName = "Microsoft.Azure.WebJobs.Extensions.Storage.Blobs",
                        MinimumAssemblyVersion = minExtensionVersion
                    }
                ];

            extensionRequirementOptions.Bundles = bundleRequirment;
            extensionRequirementOptions.Extensions = extensionRequirements;
            return extensionRequirementOptions;
        }

        private string InstallExtensions(params ExtensionInstall[] extensions)
        {
            string binPath = Path.Combine(_directory.Path, "bin");
            Directory.CreateDirectory(binPath);

            JArray jArray = [];
            foreach (ExtensionInstall e in extensions)
            {
                ExtensionReference reference = e.GetReference();
                jArray.Add(JObject.FromObject(reference));
                e.CopyTo(binPath);
            }

            JObject jObject = new()
            {
                { "extensions", jArray },
            };

            File.WriteAllText(Path.Combine(binPath, "extensions.json"), jObject.ToString());
            return binPath;
        }

        private void SetupMetadataManager(string language)
        {
            FunctionMetadata functionMetadata = new();
            functionMetadata.Bindings.Add(new BindingMetadata() { Type = "blob" });
            functionMetadata.Language = language;
            ImmutableArray<FunctionMetadata> result = [functionMetadata];
            _metadataManager.Setup(m => m.GetFunctionMetadata(true, true, false)).Returns(result);
        }

        private ScriptStartupTypeLocator CreateSystemUnderTest(
            string rootPath = null,
            ExtensionRequirementOptions extensionRequirements = null,
            WorkerConfigCacheInvalidator workerConfigCacheInvalidator = null)
        {
            LoggerFactory factory = new();
            factory.AddProvider(_loggerProvider);
            OptionsWrapper<ExtensionRequirementOptions> optionsWrapper = new(extensionRequirements ?? new());
            workerConfigCacheInvalidator ??= new(null, null);
            return new(
                rootPath ?? _directory.Path,
                factory.CreateLogger<ScriptStartupTypeLocator>(),
                _bundleManager.Object,
                _metadataManager.Object,
                _metricsLogger,
                _environment,
                optionsWrapper,
                workerConfigCacheInvalidator);
        }

        private bool AreExpectedMetricsGenerated()
        {
            return _metricsLogger.EventsBegan.Contains(MetricEventNames.ParseExtensions) && _metricsLogger.EventsEnded.Contains(MetricEventNames.ParseExtensions);
        }

        private class ExtensionInstall(string name, Type startupType, params string[] bindings)
        {
            public string HintPath { get; init; }

            public static ExtensionInstall BlobStorage(bool includeBinding = false)
            {
                string[] bindings = includeBinding ? ["blob"] : [];
                return new("AzureStorageBlobs", typeof(AzureStorageBlobsWebJobsStartup), bindings);
            }

            public static ExtensionInstall QueueStorage(bool includeBinding = false)
            {
                string[] bindings = includeBinding ? ["queue"] : [];
                return new("AzureStorageQueues", typeof(AzureStorageQueuesWebJobsStartup), bindings);
            }

            public static ExtensionInstall Timers()
            {
                return new("Timers", typeof(ExtensionsWebJobsStartup));
            }

            public static ExtensionInstall Http()
            {
                return new("Http", typeof(HttpWebJobsStartup));
            }

            public ExtensionReference GetReference()
            {
                ExtensionReference reference = new()
                {
                    Name = name,
                    TypeName = startupType.AssemblyQualifiedName,
                    HintPath = HintPath,
                };
                foreach (string binding in bindings ?? Enumerable.Empty<string>())
                {
                    reference.Bindings.Add(binding);
                }

                return reference;
            }

            public void CopyTo(string path)
            {
                string file = startupType.Assembly.Location;
                string destination = Path.Combine(path, Path.GetFileName(file));
                if (!File.Exists(destination))
                {
                    File.Copy(file, destination);
                }
            }
        }
    }
}
