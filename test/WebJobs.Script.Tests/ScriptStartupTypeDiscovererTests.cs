// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Azure.WebJobs.Extensions;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.Storage;
using Microsoft.Azure.WebJobs.Script.DependencyInjection;
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
                var discoverer = new ScriptStartupTypeLocator(directory.Path, testLogger, mockExtensionBundleManager.Object);

                // Act
                var types = await discoverer.GetExtensionsStartupTypesAsync();
                var traces = testLoggerProvider.GetAllLogMessages();

                // Assert
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

            using (var directory = new TempDirectory())
            {
                TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
                LoggerFactory factory = new LoggerFactory();
                factory.AddProvider(testLoggerProvider);
                var testLogger = factory.CreateLogger<ScriptStartupTypeLocator>();
                var discoverer = new ScriptStartupTypeLocator(string.Empty, testLogger, mockExtensionBundleManager.Object);

                // Act
                var types = await discoverer.GetExtensionsStartupTypesAsync();
                var traces = testLoggerProvider.GetAllLogMessages();

                // Assert
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

            using (var directory = new TempDirectory())
            {
                var binPath = Path.Combine(directory.Path, "bin");
                mockExtensionBundleManager.Setup(e => e.GetExtensionBundlePath()).ReturnsAsync(directory.Path);
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
                var discoverer = new ScriptStartupTypeLocator(directory.Path, testLogger, mockExtensionBundleManager.Object);

                // Act
                var types = await discoverer.GetExtensionsStartupTypesAsync();
                var traces = testLoggerProvider.GetAllLogMessages();

                // Assert
                Assert.Single(types);
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
            mockExtensionBundleManager.Setup(e => e.GetExtensionBundlePath()).ReturnsAsync(string.Empty);

            TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
            LoggerFactory factory = new LoggerFactory();
            factory.AddProvider(testLoggerProvider);
            var testLogger = factory.CreateLogger<ScriptStartupTypeLocator>();
            var discoverer = new ScriptStartupTypeLocator(string.Empty, testLogger, mockExtensionBundleManager.Object);

            // Act
            var types = await discoverer.GetExtensionsStartupTypesAsync();
            var traces = testLoggerProvider.GetAllLogMessages();

            // Assert
            Assert.True(traces.Any(m => string.Equals(m.FormattedMessage, $"Unable to find or download extension bundle")));
            Assert.NotNull(types);
            Assert.Equal(types.Count(), 0);
        }
    }
}
