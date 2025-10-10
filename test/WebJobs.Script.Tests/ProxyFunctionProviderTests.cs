// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ProxyFunctionProviderTests
    {
        [Fact]
        public async Task ProxyMetadata_WhenProxyFileChanges_IsRefreshed()
        {
            using (var tempDirectory = new TempDirectory())
            {
                var testProxiesPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\Proxies");
                var options = new OptionsWrapper<ScriptJobHostOptions>(new ScriptJobHostOptions
                {
                    RootScriptPath = tempDirectory.Path
                });

                var environment = new TestEnvironment(new Dictionary<string, string>
                {
                    { EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableProxies },
                });
                var eventManager = new ScriptEventManager();

                var provider = new ProxyFunctionProvider(options, environment, eventManager, NullLoggerFactory.Instance);

                // Get metadata before proxies exist
                ImmutableArray<FunctionMetadata> proxyMetadata1 = await provider.GetFunctionMetadataAsync();
                ImmutableArray<FunctionMetadata> proxyMetadata2 = await provider.GetFunctionMetadataAsync();

                Assert.True(proxyMetadata2.IsDefaultOrEmpty);
                Assert.True(proxyMetadata1.IsDefaultOrEmpty);

                // Update our proxies definition
                FileUtility.CopyDirectory(testProxiesPath, tempDirectory.Path);

                // Simulate a file change notification
                eventManager.Publish(new FileEvent(EventSources.ScriptFiles,
                    new FileSystemEventArgs(WatcherChangeTypes.Changed, tempDirectory.Path, ScriptConstants.ProxyMetadataFileName)));

                ImmutableArray<FunctionMetadata> proxyMetadata3 = await provider.GetFunctionMetadataAsync();

                var proxyClient = ((ProxyFunctionMetadata)proxyMetadata3.First()).ProxyClient;

                Assert.True(proxyMetadata3.Select(p => (p as ProxyFunctionMetadata).ProxyClient).All(c => c.Equals(proxyClient)));
                Assert.Equal(20, proxyMetadata3.Length);
            }
        }

        [Fact]
        public async Task ProxyFunctionProvider_WhenProxiesEnabled_EmitsDiagnosticWarning()
        {
            using (var tempDirectory = new TempDirectory())
            {
                var testProxiesPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\Proxies");
                var options = new OptionsWrapper<ScriptJobHostOptions>(new ScriptJobHostOptions
                {
                    RootScriptPath = tempDirectory.Path
                });

                var environment = new TestEnvironment(new Dictionary<string, string>
                {
                    { EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableProxies },
                });
                var eventManager = new ScriptEventManager();
                var loggerFactory = new LoggerFactory();
                var testLoggerProvider = new TestLoggerProvider();
                loggerFactory.AddProvider(testLoggerProvider);

                var provider = new ProxyFunctionProvider(options, environment, eventManager, loggerFactory);

                // Copy proxies definition to trigger the warning
                FileUtility.CopyDirectory(testProxiesPath, tempDirectory.Path);

                // Get metadata to trigger loading of proxies and the diagnostic warning
                ImmutableArray<FunctionMetadata> proxyMetadata = await provider.GetFunctionMetadataAsync();

                // Verify proxies were loaded
                Assert.NotEmpty(proxyMetadata);

                var warningLog = testLoggerProvider.GetAllLogMessages().FirstOrDefault(m =>
                    m.Level == LogLevel.Warning &&
                    m.State is IDictionary<string, object> state &&
                    state.ContainsKey(ScriptConstants.ErrorCodeKey) &&
                    state[ScriptConstants.ErrorCodeKey].ToString() == DiagnosticEventConstants.DeprecatedProxiesErrorCode);

                Assert.NotNull(warningLog);
                Assert.Contains("Azure Functions Proxies are deprecated", warningLog.FormattedMessage);
                Assert.Contains("September 30, 2025", warningLog.FormattedMessage);
            }
        }
    }
}
