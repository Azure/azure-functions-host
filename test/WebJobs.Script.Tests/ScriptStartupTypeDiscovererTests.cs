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
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

                var languageWorkerOptions = new OptionsWrapper<LanguageWorkerOptions>(new LanguageWorkerOptions());

                var discoverer = new ScriptStartupTypeLocator(directory.Path, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger, languageWorkerOptions);

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
                var languageWorkerOptions = new OptionsWrapper<LanguageWorkerOptions>(new LanguageWorkerOptions());
                var discoverer = new ScriptStartupTypeLocator(string.Empty, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger, languageWorkerOptions);

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
                var languageWorkerOptions = new OptionsWrapper<LanguageWorkerOptions>(new LanguageWorkerOptions());
                var discoverer = new ScriptStartupTypeLocator(directory.Path, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger, languageWorkerOptions);

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
            var languageWorkerOptions = new OptionsWrapper<LanguageWorkerOptions>(new LanguageWorkerOptions());
            var discoverer = new ScriptStartupTypeLocator(string.Empty, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger, languageWorkerOptions);

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
                var languageWorkerOptions = new OptionsWrapper<LanguageWorkerOptions>(new LanguageWorkerOptions());
                var discoverer = new ScriptStartupTypeLocator(directory.Path, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger, languageWorkerOptions);

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
                var languageWorkerOptions = new OptionsWrapper<LanguageWorkerOptions>(new LanguageWorkerOptions());
                var discoverer = new ScriptStartupTypeLocator(directory.Path, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger, languageWorkerOptions);

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
                var languageWorkerOptions = new OptionsWrapper<LanguageWorkerOptions>(new LanguageWorkerOptions());
                var discoverer = new ScriptStartupTypeLocator(directory.Path, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger, languageWorkerOptions);

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
                var languageWorkerOptions = new OptionsWrapper<LanguageWorkerOptions>(new LanguageWorkerOptions());
                var discoverer = new ScriptStartupTypeLocator(directory.Path, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger, languageWorkerOptions);

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
                var languageWorkerOptions = new OptionsWrapper<LanguageWorkerOptions>(new LanguageWorkerOptions());
                var discoverer = new ScriptStartupTypeLocator(directory.Path, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger, languageWorkerOptions);

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
                var languageWorkerOptions = new OptionsWrapper<LanguageWorkerOptions>(new LanguageWorkerOptions());
                var discoverer = new ScriptStartupTypeLocator(directory.Path, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger, languageWorkerOptions);

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
                var languageWorkerOptions = new OptionsWrapper<LanguageWorkerOptions>(new LanguageWorkerOptions());
                var discoverer = new ScriptStartupTypeLocator(directory.Path, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger, languageWorkerOptions);

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
                var languageWorkerOptions = new OptionsWrapper<LanguageWorkerOptions>(new LanguageWorkerOptions());
                var discoverer = new ScriptStartupTypeLocator(directory.Path, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger, languageWorkerOptions);

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

        [Fact]
        public async Task GetExtensionsStartupTypes_WorkerIndexing_BypassesSelectiveLoading()
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

                // mock worker config and environment variables to make host choose worker indexing
                RpcWorkerConfig workerConfig = new RpcWorkerConfig() { Description = TestHelpers.GetTestWorkerDescription("python", "none", true) };
                var tempOptions = new LanguageWorkerOptions();
                tempOptions.WorkerConfigs = new List<RpcWorkerConfig>();
                tempOptions.WorkerConfigs.Add(workerConfig);

                var languageWorkerOptions = new OptionsWrapper<LanguageWorkerOptions>(tempOptions);
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "python");
                var discoverer = new ScriptStartupTypeLocator(directory.Path, testLogger, mockExtensionBundleManager.Object, mockFunctionMetadataManager, testMetricsLogger, languageWorkerOptions);

                // Act
                var types = await discoverer.GetExtensionsStartupTypesAsync();
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, null);
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, null);

                //Assert that filtering did not take place because of worker indexing
                Assert.True(types.Count() == 2);
                Assert.Equal(typeof(AzureStorageWebJobsStartup).FullName, types.ElementAt(0).FullName);
                Assert.Equal(typeof(AzureStorageWebJobsStartup).FullName, types.ElementAt(1).FullName);
            }
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
            functionMetadataManager.Setup(e => e.GetFunctionMetadata(true, true, false, null)).Returns(functionMetadataCollection.ToImmutableArray());
            return functionMetadataManager.Object;
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
