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
using Microsoft.Azure.WebJobs.Script.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json.Linq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ScriptStartupTypeDiscovererTests
    {
        [Fact]
        public async Task GetExtensionsStartupTypes_FiltersBuiltinExtensionsAsync()
        {
            var references = new[]
            {
                new ExtensionReference { Name = "Http", TypeName = typeof(HttpWebJobsStartup).AssemblyQualifiedName },
                new ExtensionReference { Name = "Timers", TypeName = typeof(ExtensionsWebJobsStartup).AssemblyQualifiedName },
                new ExtensionReference { Name = "Storage", TypeName = typeof(AzureStorageWebJobsStartup).AssemblyQualifiedName },
            };

            var extensions = new JObject
            {
                { "extensions", JArray.FromObject(references) }
            };

            var mockExtensionBundleManager = new Mock<IExtensionBundleManager>();
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();
            mockExtensionBundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(false);

            using (var directory = new TempDirectory())
            {
                var binPath = Path.Combine(directory.Path, "bin");
                Directory.CreateDirectory(binPath);

                void CopyToBin(string path)
                {
                    File.Copy(path, Path.Combine(binPath, Path.GetFileName(path)));
                }

                CopyToBin(typeof(HttpWebJobsStartup).Assembly.Location);
                CopyToBin(typeof(ExtensionsWebJobsStartup).Assembly.Location);
                CopyToBin(typeof(AzureStorageWebJobsStartup).Assembly.Location);

                File.WriteAllText(Path.Combine(binPath, "extensions.json"), extensions.ToString());

                TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
                LoggerFactory factory = new LoggerFactory();
                factory.AddProvider(testLoggerProvider);
                var testLogger = factory.CreateLogger<ScriptStartupTypeLocator>();

                var mockFunctionMetadataManager = GetTestFunctionMetadataManager(ImmutableArray<FunctionMetadata>.Empty);
                var mockEnvironment = GetTestApplicationInsightsEnvironment(null, null);
                var discoverer = new ScriptStartupTypeLocator(directory.Path, mockEnvironment, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger);

                // Act
                var types = await discoverer.GetExtensionsStartupTypesAsync();
                var traces = testLoggerProvider.GetAllLogMessages();

                // Assert
                AreExpectedMetricsGenerated(testMetricsLogger);
                Assert.Single(types);
                Assert.Equal(typeof(AzureStorageWebJobsStartup).FullName, types.Single().FullName);
                Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"The extension startup type '{references[0].TypeName}' belongs to a builtin extension")));
                Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"The extension startup type '{references[1].TypeName}' belongs to a builtin extension")));
            }
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_ExtensionBundleReturnsNullPath_ReturnsNull()
        {
            var mockExtensionBundleManager = new Mock<IExtensionBundleManager>();
            mockExtensionBundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            mockExtensionBundleManager.Setup(e => e.GetExtensionBundlePath()).ReturnsAsync(string.Empty);
            mockExtensionBundleManager.Setup(e => e.GetExtensionBundleDetails()).Returns(Task.FromResult(GetV2BundleDetails()));

            using (var directory = new TempDirectory())
            {
                TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
                LoggerFactory factory = new LoggerFactory();
                TestMetricsLogger testMetricsLogger = new TestMetricsLogger();
                factory.AddProvider(testLoggerProvider);
                var testLogger = factory.CreateLogger<ScriptStartupTypeLocator>();

                var mockFunctionMetadataManager = GetTestFunctionMetadataManager(ImmutableArray<FunctionMetadata>.Empty);
                var mockEnvironment = GetTestApplicationInsightsEnvironment(null, null);
                var discoverer = new ScriptStartupTypeLocator(string.Empty, mockEnvironment, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger);

                // Act
                var types = await discoverer.GetExtensionsStartupTypesAsync();
                var traces = testLoggerProvider.GetAllLogMessages();

                // Assert
                AreExpectedMetricsGenerated(testMetricsLogger);
                Assert.NotNull(types);
                Assert.Equal(types.Count(), 0);
                Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"Unable to find or download extension bundle")));
            }
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_ValidExtensionBundle_FiltersBuiltinExtensionsAsync()
        {
            var references = new[]
            {
                new ExtensionReference { Name = "Http", TypeName = typeof(HttpWebJobsStartup).AssemblyQualifiedName },
                new ExtensionReference { Name = "Timers", TypeName = typeof(ExtensionsWebJobsStartup).AssemblyQualifiedName },
                new ExtensionReference { Name = "Storage", TypeName = typeof(AzureStorageWebJobsStartup).AssemblyQualifiedName },
            };

            var extensions = new JObject
            {
                { "extensions", JArray.FromObject(references) }
            };

            var mockExtensionBundleManager = new Mock<IExtensionBundleManager>();
            mockExtensionBundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            mockExtensionBundleManager.Setup(e => e.GetExtensionBundleDetails()).Returns(Task.FromResult(GetV2BundleDetails()));
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            using (var directory = new TempDirectory())
            {
                var binPath = Path.Combine(directory.Path, "bin");
                mockExtensionBundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).Returns(Task.FromResult(binPath));
                Directory.CreateDirectory(binPath);

                void CopyToBin(string path)
                {
                    File.Copy(path, Path.Combine(binPath, Path.GetFileName(path)));
                }

                CopyToBin(typeof(HttpWebJobsStartup).Assembly.Location);
                CopyToBin(typeof(ExtensionsWebJobsStartup).Assembly.Location);
                CopyToBin(typeof(AzureStorageWebJobsStartup).Assembly.Location);

                File.WriteAllText(Path.Combine(binPath, "extensions.json"), extensions.ToString());

                TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
                LoggerFactory factory = new LoggerFactory();
                factory.AddProvider(testLoggerProvider);
                var testLogger = factory.CreateLogger<ScriptStartupTypeLocator>();

                var mockFunctionMetadataManager = GetTestFunctionMetadataManager(ImmutableArray<FunctionMetadata>.Empty);
                var mockEnvironment = GetTestApplicationInsightsEnvironment(null, null);
                var discoverer = new ScriptStartupTypeLocator(directory.Path, mockEnvironment, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger);

                // Act
                var types = await discoverer.GetExtensionsStartupTypesAsync();
                var traces = testLoggerProvider.GetAllLogMessages();

                // Assert
                Assert.Single(types);
                AreExpectedMetricsGenerated(testMetricsLogger);
                Assert.Equal(typeof(AzureStorageWebJobsStartup).FullName, types.Single().FullName);
                Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"The extension startup type '{references[0].TypeName}' belongs to a builtin extension")));
                Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"The extension startup type '{references[1].TypeName}' belongs to a builtin extension")));
            }
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_UnableToDownloadExtensionBundle_ReturnsNull()
        {
            var mockExtensionBundleManager = new Mock<IExtensionBundleManager>();
            mockExtensionBundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
            mockExtensionBundleManager.Setup(e => e.GetExtensionBundleDetails()).Returns(Task.FromResult(GetV2BundleDetails()));
            mockExtensionBundleManager.Setup(e => e.GetExtensionBundlePath()).ReturnsAsync(string.Empty);

            TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
            LoggerFactory factory = new LoggerFactory();
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();
            factory.AddProvider(testLoggerProvider);
            var testLogger = factory.CreateLogger<ScriptStartupTypeLocator>();

            var mockFunctionMetadataManager = GetTestFunctionMetadataManager(ImmutableArray<FunctionMetadata>.Empty);
            var mockEnvironment = GetTestApplicationInsightsEnvironment(null, null);
            var discoverer = new ScriptStartupTypeLocator(string.Empty, mockEnvironment, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger);

            // Act
            var types = await discoverer.GetExtensionsStartupTypesAsync();
            var traces = testLoggerProvider.GetAllLogMessages();

            // Assert
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"Unable to find or download extension bundle")));
            AreExpectedMetricsGenerated(testMetricsLogger);
            Assert.NotNull(types);
            Assert.Equal(types.Count(), 0);
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_BundlesConfiguredBindingsNotConfigured_LoadsAllExtensions()
        {
            var storageExtensionReference = new ExtensionReference { Name = "Storage", TypeName = typeof(AzureStorageWebJobsStartup).AssemblyQualifiedName };
            var sendGridExtensionReference = new ExtensionReference { Name = "SendGrid", TypeName = typeof(AzureStorageWebJobsStartup).AssemblyQualifiedName };
            var references = new[] { storageExtensionReference, sendGridExtensionReference };
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            var extensions = new JObject
            {
                { "extensions", JArray.FromObject(references) }
            };

            using (var directory = new TempDirectory())
            {
                var binPath = Path.Combine(directory.Path, "bin");
                Directory.CreateDirectory(binPath);

                void CopyToBin(string path)
                {
                    File.Copy(path, Path.Combine(binPath, Path.GetFileName(path)));
                }

                CopyToBin(typeof(AzureStorageWebJobsStartup).Assembly.Location);

                File.WriteAllText(Path.Combine(binPath, "extensions.json"), extensions.ToString());

                TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
                LoggerFactory factory = new LoggerFactory();
                factory.AddProvider(testLoggerProvider);
                var testLogger = factory.CreateLogger<ScriptStartupTypeLocator>();

                var mockFunctionMetadataManager = GetTestFunctionMetadataManager();
                var mockExtensionBundleManager = new Mock<IExtensionBundleManager>();
                mockExtensionBundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
                mockExtensionBundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).Returns(Task.FromResult(binPath));
                mockExtensionBundleManager.Setup(e => e.GetExtensionBundleDetails()).Returns(Task.FromResult(GetV2BundleDetails()));

                var mockEnvironment = GetTestApplicationInsightsEnvironment(null, null);
                var discoverer = new ScriptStartupTypeLocator(directory.Path, mockEnvironment, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger);

                // Act
                var types = await discoverer.GetExtensionsStartupTypesAsync();

                // Assert
                AreExpectedMetricsGenerated(testMetricsLogger);
                Assert.Equal(types.Count(), 2);
                Assert.Equal(typeof(AzureStorageWebJobsStartup).FullName, types.FirstOrDefault().FullName);
            }
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_BundlesNotConfiguredBindingsNotConfigured_LoadsAllExtensions()
        {
            var references = new[]
            {
                new ExtensionReference { Name = "Storage", TypeName = typeof(AzureStorageWebJobsStartup).AssemblyQualifiedName }
            };

            var extensions = new JObject
            {
                { "extensions", JArray.FromObject(references) }
            };

            var mockExtensionBundleManager = new Mock<IExtensionBundleManager>();
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();
            mockExtensionBundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(false);

            using (var directory = new TempDirectory())
            {
                var binPath = Path.Combine(directory.Path, "bin");
                Directory.CreateDirectory(binPath);

                void CopyToBin(string path)
                {
                    File.Copy(path, Path.Combine(binPath, Path.GetFileName(path)));
                }

                CopyToBin(typeof(AzureStorageWebJobsStartup).Assembly.Location);

                File.WriteAllText(Path.Combine(binPath, "extensions.json"), extensions.ToString());

                TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
                LoggerFactory factory = new LoggerFactory();
                factory.AddProvider(testLoggerProvider);
                var testLogger = factory.CreateLogger<ScriptStartupTypeLocator>();

                // mock Function metadata
                var mockFunctionMetadataManager = GetTestFunctionMetadataManager(ImmutableArray<FunctionMetadata>.Empty);
                var mockEnvironment = GetTestApplicationInsightsEnvironment(null, null);
                var discoverer = new ScriptStartupTypeLocator(directory.Path, mockEnvironment, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger);

                // Act
                var types = await discoverer.GetExtensionsStartupTypesAsync();

                // Assert
                AreExpectedMetricsGenerated(testMetricsLogger);
                Assert.Single(types);
                Assert.Equal(typeof(AzureStorageWebJobsStartup).FullName, types.Single().FullName);
            }
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_BundlesConfiguredBindingsConfigured_PerformsSelectiveLoading()
        {
            var storageExtensionReference = new ExtensionReference { Name = "Storage", TypeName = typeof(AzureStorageWebJobsStartup).AssemblyQualifiedName };
            storageExtensionReference.Bindings.Add("blob");
            var sendGridExtensionReference = new ExtensionReference { Name = "SendGrid", TypeName = typeof(AzureStorageWebJobsStartup).AssemblyQualifiedName };
            sendGridExtensionReference.Bindings.Add("sendGrid");
            var references = new[] { storageExtensionReference, sendGridExtensionReference };
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            var extensions = new JObject
            {
                { "extensions", JArray.FromObject(references) }
            };

            using (var directory = new TempDirectory())
            {
                var binPath = Path.Combine(directory.Path, "bin");
                Directory.CreateDirectory(binPath);

                void CopyToBin(string path)
                {
                    File.Copy(path, Path.Combine(binPath, Path.GetFileName(path)));
                }

                CopyToBin(typeof(AzureStorageWebJobsStartup).Assembly.Location);

                File.WriteAllText(Path.Combine(binPath, "extensions.json"), extensions.ToString());

                TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
                LoggerFactory factory = new LoggerFactory();
                factory.AddProvider(testLoggerProvider);
                var testLogger = factory.CreateLogger<ScriptStartupTypeLocator>();

                var mockFunctionMetadataManager = GetTestFunctionMetadataManager();

                var mockExtensionBundleManager = new Mock<IExtensionBundleManager>();
                mockExtensionBundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
                mockExtensionBundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).Returns(Task.FromResult(binPath));
                mockExtensionBundleManager.Setup(e => e.GetExtensionBundleDetails()).Returns(Task.FromResult(GetV2BundleDetails()));
                var mockEnvironment = GetTestApplicationInsightsEnvironment(null, null);
                var discoverer = new ScriptStartupTypeLocator(directory.Path, mockEnvironment, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger);

                // Act
                var types = await discoverer.GetExtensionsStartupTypesAsync();

                //Assert
                AreExpectedMetricsGenerated(testMetricsLogger);
                Assert.Single(types);
                Assert.Equal(typeof(AzureStorageWebJobsStartup).FullName, types.Single().FullName);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetExtensionsStartupTypes_LegacyBundles_UsesExtensionBundleBinaries(bool hasPrecompiledFunctions)
        {
            using (var directory = GetTempDirectory())
            {
                var binPath = Path.Combine(directory.Path, "bin");
                TestMetricsLogger testMetricsLogger = new TestMetricsLogger();
                var testLogger = GetTestLogger();

                var mockExtensionBundleManager = new Mock<IExtensionBundleManager>();
                mockExtensionBundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
                mockExtensionBundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).Returns(Task.FromResult(binPath));
                mockExtensionBundleManager.Setup(e => e.IsLegacyExtensionBundle()).Returns(true);
                mockExtensionBundleManager.Setup(e => e.GetExtensionBundleDetails()).Returns(Task.FromResult(GetV2BundleDetails()));

                var mockFunctionMetadataManager = GetTestFunctionMetadataManager(hasPrecompiledFunction: hasPrecompiledFunctions);
                var mockEnvironment = GetTestApplicationInsightsEnvironment(null, null);
                var discoverer = new ScriptStartupTypeLocator(directory.Path, mockEnvironment, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger);

                // Act
                var types = await discoverer.GetExtensionsStartupTypesAsync();

                //Assert
                AreExpectedMetricsGenerated(testMetricsLogger);
                Assert.Single(types);
                Assert.Equal(typeof(AzureStorageWebJobsStartup).FullName, types.Single().FullName);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetExtensionsStartupTypes_NonLegacyBundles_UsesBundlesForNonPrecompiledFunctions(bool hasPrecompiledFunctions)
        {
            using (var directory = GetTempDirectory())
            {
                TestMetricsLogger testMetricsLogger = new TestMetricsLogger();
                var testLogger = GetTestLogger();

                string bundlePath = hasPrecompiledFunctions ? "FakePath" : directory.Path;
                var binPath = Path.Combine(directory.Path, "bin");

                var mockExtensionBundleManager = new Mock<IExtensionBundleManager>();
                mockExtensionBundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
                mockExtensionBundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).Returns(Task.FromResult(binPath));
                mockExtensionBundleManager.Setup(e => e.IsLegacyExtensionBundle()).Returns(false);
                mockExtensionBundleManager.Setup(e => e.GetExtensionBundleDetails()).Returns(Task.FromResult(GetV2BundleDetails()));

                var mockFunctionMetadataManager = GetTestFunctionMetadataManager(hasPrecompiledFunction: hasPrecompiledFunctions);
                var mockEnvironment = GetTestApplicationInsightsEnvironment(null, null);
                var discoverer = new ScriptStartupTypeLocator(directory.Path, mockEnvironment, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger);

                // Act
                var types = await discoverer.GetExtensionsStartupTypesAsync();

                //Assert
                AreExpectedMetricsGenerated(testMetricsLogger);
                Assert.Single(types);
                Assert.Equal(typeof(AzureStorageWebJobsStartup).FullName, types.Single().FullName);
            }
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_BundlesNotConfiguredBindingsConfigured_LoadsAllExtensions()
        {
            var storageExtensionReference = new ExtensionReference { Name = "Storage", TypeName = typeof(AzureStorageWebJobsStartup).AssemblyQualifiedName };
            storageExtensionReference.Bindings.Add("blob");
            var sendGridExtensionReference = new ExtensionReference { Name = "SendGrid", TypeName = typeof(AzureStorageWebJobsStartup).AssemblyQualifiedName };
            sendGridExtensionReference.Bindings.Add("sendGrid");
            var references = new[] { storageExtensionReference, sendGridExtensionReference };
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            var extensions = new JObject
            {
                { "extensions", JArray.FromObject(references) }
            };

            using (var directory = new TempDirectory())
            {
                var binPath = Path.Combine(directory.Path, "bin");
                Directory.CreateDirectory(binPath);

                void CopyToBin(string path)
                {
                    File.Copy(path, Path.Combine(binPath, Path.GetFileName(path)));
                }

                CopyToBin(typeof(AzureStorageWebJobsStartup).Assembly.Location);

                File.WriteAllText(Path.Combine(binPath, "extensions.json"), extensions.ToString());

                TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
                LoggerFactory factory = new LoggerFactory();
                factory.AddProvider(testLoggerProvider);
                var testLogger = factory.CreateLogger<ScriptStartupTypeLocator>();

                var mockExtensionBundleManager = new Mock<IExtensionBundleManager>();
                mockExtensionBundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(false);

                var mockFunctionMetadataManager = GetTestFunctionMetadataManager();
                var mockEnvironment = GetTestApplicationInsightsEnvironment(null, null);
                var discoverer = new ScriptStartupTypeLocator(directory.Path, mockEnvironment, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger);

                // Act
                var types = await discoverer.GetExtensionsStartupTypesAsync();

                // Assert
                AreExpectedMetricsGenerated(testMetricsLogger);
                Assert.Equal(types.Count(), 2);
                Assert.Equal(typeof(AzureStorageWebJobsStartup).FullName, types.FirstOrDefault().FullName);
            }
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_RejectsBundleBelowMinimumVersion()
        {
            using (var directory = GetTempDirectory())
            {
                TestMetricsLogger testMetricsLogger = new TestMetricsLogger();
                TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
                LoggerFactory factory = new LoggerFactory();
                factory.AddProvider(testLoggerProvider);
                var testLogger = factory.CreateLogger<ScriptStartupTypeLocator>();

                var binPath = Path.Combine(directory.Path, "bin");

                var mockExtensionBundleManager = new Mock<IExtensionBundleManager>();
                mockExtensionBundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(true);
                mockExtensionBundleManager.Setup(e => e.GetExtensionBundleBinPathAsync()).Returns(Task.FromResult(binPath));
                mockExtensionBundleManager.Setup(e => e.IsLegacyExtensionBundle()).Returns(false);
                mockExtensionBundleManager.Setup(e => e.GetExtensionBundleDetails()).Returns(Task.FromResult(GetV2BundleDetails("2.1.0")));

                var mockFunctionMetadataManager = GetTestFunctionMetadataManager();
                var mockEnvironment = GetTestApplicationInsightsEnvironment(null, null);
                var discoverer = new ScriptStartupTypeLocator(directory.Path, mockEnvironment, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger);

                // Act
                var exception = await Assert.ThrowsAsync<HostInitializationException>(async () => await discoverer.GetExtensionsStartupTypesAsync());
                var traces = testLoggerProvider.GetAllLogMessages();

                // Assert
                Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"Referenced bundle Microsoft.Azure.Functions.ExtensionBundle of version 2.1.0 does not meet the required minimum version of 2.6.1. Update your extension bundle reference in host.json to reference 2.6.1 or later.")));
            }
        }

        [Fact]
        public async Task GetExtensionsStartupTypes_RejectsExtensionsBelowMinimumVersion()
        {
            var mockExtensionBundleManager = new Mock<IExtensionBundleManager>();
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();
            mockExtensionBundleManager.Setup(e => e.IsExtensionBundleConfigured()).Returns(false);

            using (var directory = new TempDirectory())
            {
                var binPath = Path.Combine(directory.Path, "bin");
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

                TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
                LoggerFactory factory = new LoggerFactory();
                factory.AddProvider(testLoggerProvider);
                var testLogger = factory.CreateLogger<ScriptStartupTypeLocator>();

                var mockFunctionMetadataManager = GetTestFunctionMetadataManager(ImmutableArray<FunctionMetadata>.Empty);
                var mockEnvironment = GetTestApplicationInsightsEnvironment(null, null);
                var discoverer = new ScriptStartupTypeLocator(directory.Path, mockEnvironment, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger);

                // Act
                var exception = await Assert.ThrowsAsync<HostInitializationException>(async () => await discoverer.GetExtensionsStartupTypesAsync());
                var traces = testLoggerProvider.GetAllLogMessages();

                // Assert

                var storageTrace = traces.FirstOrDefault(m => m.FormattedMessage.StartsWith("ExtensionStartupType AzureStorageWebJobsStartup"));
                Assert.NotNull(storageTrace);
                Assert.Equal("ExtensionStartupType AzureStorageWebJobsStartup from assembly 'Microsoft.Azure.WebJobs.Extensions.Storage, Version=3.0.10.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35' does not meet the required minimum version of 4.0.4.0. Update your NuGet package reference for Microsoft.Azure.WebJobs.Extensions.Storage to 4.0.4 or later.",
                    storageTrace.FormattedMessage);
            }
        }

        [Theory]
        [InlineData("ApplicationInsightsConnectionStringValue")]
        [InlineData(null)]
        public void IsValidBindingMatch_ApplicationInsightsSetting_UsesConfiguration(string applicationInsightsSetting)
        {
            var applicationInsightsExtensionReference = new ExtensionReference { Name = "ApplicationInsights", TypeName = "Microsoft.Azure.WebJobs.Extensions.ApplicationInsights.ApplicationInsightsWebJobsStartup, Microsoft.Azure.WebJobs.Extensions.ApplicationInsights, Version=1.0.0.0, Culture=neutral, PublicKeyToken=9475d07f10cb09df" };
            applicationInsightsExtensionReference.Bindings.Add("_");

            TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
            LoggerFactory factory = new LoggerFactory();
            factory.AddProvider(testLoggerProvider);
            var testLogger = factory.CreateLogger<ScriptStartupTypeLocator>();
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            var mockExtensionBundleManager = new Mock<IExtensionBundleManager>();

            var mockFunctionMetadataManager = GetTestFunctionMetadataManager();
            var mockEnvironment = GetTestApplicationInsightsEnvironment(applicationInsightsSetting, applicationInsightsSetting);
            var discoverer = new ScriptStartupTypeLocator(string.Empty, mockEnvironment, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger);

            // Act
            var result = ScriptStartupTypeLocator.IsValidBindingMatch(applicationInsightsExtensionReference, new HashSet<string>(), mockEnvironment);

            // Assert
            Assert.Equal(result, !string.IsNullOrEmpty(applicationInsightsSetting));
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public void ValidateApplicationInsightsConfig_ApplicationInsightsConfiguredCorrectly_NoException(bool extensionInstalled, bool settingPresent)
        {
            var applicationInsightsType = new Mock<Type>(MockBehavior.Strict);
            applicationInsightsType.Setup(t => t.Name).Returns("ApplicationInsightsWebJobsStartup");

            TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
            LoggerFactory factory = new LoggerFactory();
            factory.AddProvider(testLoggerProvider);
            var testLogger = factory.CreateLogger<ScriptStartupTypeLocator>();
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            var mockExtensionBundleManager = new Mock<IExtensionBundleManager>();

            var mockFunctionMetadataManager = GetTestFunctionMetadataManager();
            var mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            var discoverer = new ScriptStartupTypeLocator(string.Empty, mockEnvironment.Object, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger);

            var types = new List<Type>();
            if (extensionInstalled)
            {
                types.Add(applicationInsightsType.Object);
            }

            // Act
            // This tests all valid possibilies to ensure no exception is thrown
            ScriptStartupTypeLocator.ValidateApplicationInsightsConfig(types, false, settingPresent, false, testLogger);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void ValidateApplicationInsightsConfig_ApplicationInsightsMisconfigured_ThrowsException(bool extensionInstalled, bool settingPresent)
        {
            var applicationInsightsType = new Mock<Type>(MockBehavior.Strict);
            applicationInsightsType.Setup(t => t.Name).Returns("ApplicationInsightsWebJobsStartup");

            TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
            LoggerFactory factory = new LoggerFactory();
            factory.AddProvider(testLoggerProvider);
            var testLogger = factory.CreateLogger<ScriptStartupTypeLocator>();
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            var mockExtensionBundleManager = new Mock<IExtensionBundleManager>();

            var mockFunctionMetadataManager = GetTestFunctionMetadataManager();
            var mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            var discoverer = new ScriptStartupTypeLocator(string.Empty, mockEnvironment.Object, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger);

            var types = new List<Type>();
            if (extensionInstalled)
            {
                types.Add(applicationInsightsType.Object);
            }

            var expectedExceptionMessage = $"{EnvironmentSettingNames.AppInsightsConnectionString} or {EnvironmentSettingNames.AppInsightsInstrumentationKey} " +
                                           $"is defined but the Application Insights Extension is not installed. Please install the Application Insights Extension. " +
                                           $"See https://aka.ms/func-applicationinsights-extension for more details.";
            if (extensionInstalled && !settingPresent)
            {
                expectedExceptionMessage = $"The Application Insights Extension is installed but is not properly configured. " +
                                           $"Please define the \"{EnvironmentSettingNames.AppInsightsConnectionString}\" app setting.";
            }

            // Act
            var exception = Assert.Throws<HostInitializationException>(() => ScriptStartupTypeLocator.ValidateApplicationInsightsConfig(types, false, settingPresent, false, testLogger));

            // Assert
            Assert.Equal(expectedExceptionMessage, exception.Message);
        }

        [Fact]
        public void ValidateApplicationInsightsConfig_LocalDevelopment_EmitsWarning()
        {
            var applicationInsightsType = new Mock<Type>(MockBehavior.Strict);
            applicationInsightsType.Setup(t => t.Name).Returns("ApplicationInsightsWebJobsStartup");

            TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
            LoggerFactory factory = new LoggerFactory();
            factory.AddProvider(testLoggerProvider);
            var testLogger = factory.CreateLogger<ScriptStartupTypeLocator>();
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            var mockExtensionBundleManager = new Mock<IExtensionBundleManager>();

            var mockFunctionMetadataManager = GetTestFunctionMetadataManager();
            var mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            var discoverer = new ScriptStartupTypeLocator(string.Empty, mockEnvironment.Object, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger);

            // Act
            ScriptStartupTypeLocator.ValidateApplicationInsightsConfig(new List<Type>(), true, false, false, testLogger);
            var traces = testLoggerProvider.GetAllLogMessages();

            // Assert

            var warning = traces.FirstOrDefault(m => m.FormattedMessage.StartsWith("In order to use Application Insights"));
            Assert.NotNull(warning);
            var expectedWarning = "In order to use Application Insights in Azure Functions V4 and above, please install the Application Insights Extension. " +
                                  "See https://aka.ms/func-applicationinsights-extension for more details.";
            Assert.Equal(expectedWarning, warning.FormattedMessage);
        }

        private IFunctionMetadataManager GetTestFunctionMetadataManager(ICollection<FunctionMetadata> metadataColection = null, bool hasPrecompiledFunction = false)
        {
            var functionMetdata = new FunctionMetadata();
            functionMetdata.Bindings.Add(new BindingMetadata() { Type = "blob" });

            if (hasPrecompiledFunction)
            {
                functionMetdata.Language = DotNetScriptTypes.DotNetAssembly;
            }

            var functionMetadataCollection = metadataColection ?? new List<FunctionMetadata>() { functionMetdata };

            var functionMetadataManager = new Mock<IFunctionMetadataManager>();
            functionMetadataManager.Setup(e => e.GetFunctionMetadata(true, true, false)).Returns(functionMetadataCollection.ToImmutableArray());
            return functionMetadataManager.Object;
        }

        private IEnvironment GetTestApplicationInsightsEnvironment(string connectionString, string appSetting)
        {
            var mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            mockEnvironment.Setup(e => e.GetEnvironmentVariable(EnvironmentSettingNames.AppInsightsConnectionString)).Returns(connectionString);
            mockEnvironment.Setup(e => e.GetEnvironmentVariable(EnvironmentSettingNames.AppInsightsInstrumentationKey)).Returns(appSetting);
            return mockEnvironment.Object;
        }

        private bool AreExpectedMetricsGenerated(TestMetricsLogger metricsLogger)
        {
            return metricsLogger.EventsBegan.Contains(MetricEventNames.ParseExtensions) && metricsLogger.EventsEnded.Contains(MetricEventNames.ParseExtensions);
        }

        private TempDirectory GetTempDirectory()
        {
            var directory = new TempDirectory();
            var storageExtensionReference = new ExtensionReference { Name = "Storage", TypeName = typeof(AzureStorageWebJobsStartup).AssemblyQualifiedName };
            storageExtensionReference.Bindings.Add("blob");
            var sendGridExtensionReference = new ExtensionReference { Name = "SendGrid", TypeName = typeof(AzureStorageWebJobsStartup).AssemblyQualifiedName };
            sendGridExtensionReference.Bindings.Add("sendGrid");
            var references = new[] { storageExtensionReference, sendGridExtensionReference };

            var extensions = new JObject
            {
                { "extensions", JArray.FromObject(references) }
            };

            var binPath = Path.Combine(directory.Path, "bin");
            Directory.CreateDirectory(binPath);

            void CopyToBin(string path)
            {
                File.Copy(path, Path.Combine(binPath, Path.GetFileName(path)));
            }

            CopyToBin(typeof(AzureStorageWebJobsStartup).Assembly.Location);

            File.WriteAllText(Path.Combine(binPath, "extensions.json"), extensions.ToString());
            return directory;
        }

        private ExtensionBundleDetails GetV2BundleDetails(string version = "2.7.0")
        {
            return new ExtensionBundleDetails
            {
                Id = "Microsoft.Azure.Functions.ExtensionBundle",
                Version = version
            };
        }

        private ILogger<ScriptStartupTypeLocator> GetTestLogger()
        {
            TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
            LoggerFactory factory = new LoggerFactory();
            factory.AddProvider(testLoggerProvider);
            var testLogger = factory.CreateLogger<ScriptStartupTypeLocator>();
            return testLogger;
        }
    }
}
