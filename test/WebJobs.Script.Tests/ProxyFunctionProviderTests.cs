// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    }
}
