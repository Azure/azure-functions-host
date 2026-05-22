// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Storage
{
    /// <summary>
    /// Repros the startup race condition where <see cref="HostAzureBlobStorageProvider"/> is asked to create a
    /// <see cref="BlobServiceClient"/> before <see cref="ActiveHostConfigurationSource"/> has loaded the script
    /// host configuration. When the identity-based AzureWebJobsStorage settings (e.g. __blobServiceUri) live only
    /// in the active host configuration, the call fails with:
    ///
    ///   "Unable to find matching constructor while trying to create an instance of BlobServiceClient.
    ///    Expected one of the follow sets of configuration parameters: 1. connectionString 2. serviceUri ..."
    ///
    /// After ActiveHostChanged fires, the same call succeeds.
    /// </summary>
    public class HostAzureBlobStorageProviderTests
    {
        private const string StorageConnection = "AzureWebJobsStorage";

        [Fact]
        public void TryCreateBlobServiceClient_BeforeActiveHostLoaded_FailsThenSucceedsAfterActiveHostChanged()
        {
            // WebHost-level configuration only contains the "credential" hint so the AzureWebJobsStorage
            // section "exists" but does NOT contain the serviceUri/accountName/connectionString. This is
            // the state when the WebHost picks up env vars but the script host config hasn't merged in.
            var webHostConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "AzureWebJobsStorage:credential", "managedidentity" }
                })
                .Build();

            // Active host configuration carries the identity-based __blobServiceUri setting.
            var activeHostConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "AzureWebJobsStorage:credential", "managedidentity" },
                    { "AzureWebJobsStorage:blobServiceUri", "https://teststorage.blob.core.windows.net" }
                })
                .Build();

            // Script host manager that simulates "active host not yet initialized" — returns null for
            // IConfiguration until SignalActiveHostLoaded is called.
            var scriptHostManager = new DeferredActiveHostScriptHostManager(activeHostConfiguration);

            var (componentFactory, logForwarder) = CreateAzureClientsServices();

            var provider = new HostAzureBlobStorageProvider(
                scriptHostManager,
                webHostConfiguration,
                new OptionsMonitorWrapper<JobHostInternalStorageOptions>(new JobHostInternalStorageOptions()),
                NullLogger<HostAzureBlobStorageProvider>.Instance,
                componentFactory,
                logForwarder);

            // Before the active host configuration is loaded, the provider must fall back to
            // AzureComponentFactory.CreateClient, which cannot find a serviceUri/connectionString.
            bool firstAttempt = provider.TryCreateBlobServiceClientFromConnection(StorageConnection, out var clientBefore);
            Assert.False(firstAttempt, "Expected BlobServiceClient creation to fail before ActiveHostConfigurationSource has loaded.");
            Assert.Null(clientBefore);

            // Simulate the script host completing initialization. ActiveHostConfigurationSource subscribes to
            // this event and reloads from the script host's IConfiguration.
            scriptHostManager.SignalActiveHostLoaded();

            bool secondAttempt = provider.TryCreateBlobServiceClientFromConnection(StorageConnection, out var clientAfter);
            Assert.True(secondAttempt, "Expected BlobServiceClient creation to succeed after ActiveHostChanged fired.");
            Assert.NotNull(clientAfter);
            Assert.Equal("teststorage", clientAfter.AccountName);
        }

        [Fact]
        public void TryCreateBlobServiceClient_AccountNameInWebHostConfig_SucceedsEvenBeforeActiveHostLoaded()
        {
            // This documents why __accountName is an effective workaround: it is recognized by the underlying
            // AzureComponentFactory fallback path, so the race condition doesn't prevent client creation.
            var webHostConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "AzureWebJobsStorage:credential", "managedidentity" },
                    { "AzureWebJobsStorage:accountName", "teststorage" }
                })
                .Build();

            var activeHostConfiguration = new ConfigurationBuilder().Build();
            var scriptHostManager = new DeferredActiveHostScriptHostManager(activeHostConfiguration);

            var (componentFactory, logForwarder) = CreateAzureClientsServices();

            var provider = new HostAzureBlobStorageProvider(
                scriptHostManager,
                webHostConfiguration,
                new OptionsMonitorWrapper<JobHostInternalStorageOptions>(new JobHostInternalStorageOptions()),
                NullLogger<HostAzureBlobStorageProvider>.Instance,
                componentFactory,
                logForwarder);

            bool result = provider.TryCreateBlobServiceClientFromConnection(StorageConnection, out var client);
            Assert.True(result, "__accountName should resolve without requiring the active host configuration.");
            Assert.NotNull(client);
            Assert.Equal("teststorage", client.AccountName);
        }

        private static (AzureComponentFactory Factory, AzureEventSourceLogForwarder LogForwarder) CreateAzureClientsServices()
        {
            IHost tempHost = new HostBuilder()
                .ConfigureServices(services => services.AddAzureClientsCore())
                .Build();

            return (tempHost.Services.GetRequiredService<AzureComponentFactory>(),
                    tempHost.Services.GetRequiredService<AzureEventSourceLogForwarder>());
        }

        /// <summary>
        /// Simulates an <see cref="IScriptHostManager"/> whose active host configuration is not yet available
        /// (returns null from IServiceProvider.GetService(IConfiguration)) until <see cref="SignalActiveHostLoaded"/>
        /// is called, at which point it returns the supplied configuration and raises ActiveHostChanged.
        /// </summary>
        private sealed class DeferredActiveHostScriptHostManager : IScriptHostManager, IServiceProvider
        {
            private readonly IConfiguration _activeHostConfiguration;
            private bool _loaded;

            public DeferredActiveHostScriptHostManager(IConfiguration activeHostConfiguration)
            {
                _activeHostConfiguration = activeHostConfiguration;
            }

            public event EventHandler HostInitializing
            {
                add { }
                remove { }
            }

            public event EventHandler<ActiveHostChangedEventArgs> ActiveHostChanged;

            public ScriptHostState State => ScriptHostState.Default;

            public Exception LastError => null;

            public IServiceProvider Services => this;

            public void SignalActiveHostLoaded()
            {
                _loaded = true;
                ActiveHostChanged?.Invoke(this, new ActiveHostChangedEventArgs(null, null));
            }

            public object GetService(Type serviceType)
            {
                if (serviceType == typeof(IConfiguration))
                {
                    return _loaded ? _activeHostConfiguration : null;
                }

                return null;
            }

            public Task RestartHostAsync(string reason, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }
        }

        private sealed class OptionsMonitorWrapper<T> : IOptionsMonitor<T>
        {
            public OptionsMonitorWrapper(T value)
            {
                CurrentValue = value;
            }

            public T CurrentValue { get; }

            public T Get(string name) => CurrentValue;

            public IDisposable OnChange(Action<T, string> listener) => null;
        }
    }
}
