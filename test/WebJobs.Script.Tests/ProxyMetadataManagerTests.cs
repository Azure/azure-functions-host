// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ProxyMetadataManagerTests
    {
        [Fact]
        public void ProxyMetadata_WhenProxyFileChanges_IsRefreshed()
        {
            using (var tempDirectory = new TempDirectory())
            {
                var testProxiesPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\Proxies");
                var options = new OptionsWrapper<ScriptJobHostOptions>(new ScriptJobHostOptions
                {
                    RootScriptPath = tempDirectory.Path
                });

                var environment = new TestEnvironment();
                var eventManager = new ScriptEventManager();

                var manager = new ProxyMetadataManager(options, environment, eventManager, NullLoggerFactory.Instance);

                // Get metadata before proxies exist
                ProxyMetadataInfo proxyMetadata1 = manager.ProxyMetadata;
                ProxyMetadataInfo proxyMetadata2 = manager.ProxyMetadata;

                Assert.True(proxyMetadata2.Functions.IsDefaultOrEmpty);
                Assert.Same(proxyMetadata1, proxyMetadata2);

                // Update our proxies definition
                FileUtility.CopyDirectory(testProxiesPath, tempDirectory.Path);

                // Simulate a file change notification
                eventManager.Publish(new FileEvent(EventSources.ScriptFiles,
                    new FileSystemEventArgs(WatcherChangeTypes.Changed, tempDirectory.Path, ScriptConstants.ProxyMetadataFileName)));

                ProxyMetadataInfo proxyMetadata3 = manager.ProxyMetadata;

                Assert.NotSame(proxyMetadata1, proxyMetadata3);

                Assert.Equal(19, proxyMetadata3.Functions.Length);
            }
        }
    }
}
