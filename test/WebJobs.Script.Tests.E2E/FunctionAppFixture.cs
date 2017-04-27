// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Xunit;

namespace WebJobs.Script.EndToEndTests
{
    public class FunctionAppFixture : IDisposable
    {
        private bool _disposed;
        private KuduClient _kuduClient;
        private readonly TrackedAssert _assert;

        public FunctionAppFixture()
        {
            Trace.Listeners.Add(new ConsoleTraceListener());

            _assert = new TrackedAssert(Telemetry);

            FunctionDefaultKey = Guid.NewGuid().ToString().ToLower();

            InitializeSite().Wait();
        }

        public TrackedAssert Assert => _assert;

        public TelemetryClient Telemetry => TelemetryContext.Client;

        public KuduClient KuduClient => _kuduClient;

        public string FunctionDefaultKey { get; }

        public string FunctionAppMasterKey { get; private set; }

        private async Task InitializeSite()
        {
            Telemetry.TrackEvent(new EventTelemetry("EnvironmentInitialization"));

            Trace.WriteLine("Initializing environment...");

            await StopSite();

            _kuduClient = new KuduClient($"https://{Settings.SiteName}.scm.azurewebsites.net", Settings.SitePublishingUser, Settings.SitePublishingPassword);

            await UpdateRuntime();

            await UpdateSiteContents();

            await StartSite();

            await InitializeFunctionKeys();

            Trace.WriteLine("Restarting site...");
            await RestartSite();

            Trace.WriteLine("Environment initialized.");
            Telemetry.TrackEvent(new EventTelemetry("EnvironmentInitialized"));
        }

        private async Task InitializeFunctionKeys()
        {
            Trace.WriteLine("Initializing keys...");

            FunctionAppMasterKey = await _kuduClient.GetFunctionsMasterKey();

            using (var keysUpdate = Telemetry.StartOperation<RequestTelemetry>("KeysUpdate"))
            {
                List<Function> functions = await _kuduClient.GetFunctions();

                using (var client = new HttpClient())
                {
                    client.BaseAddress = Settings.SiteBaseAddress;

                    var requests = new List<Task<HttpResponseMessage>>();
                    foreach (var function in functions.Where(f => f.Configuration.Bindings.Any(b => string.Equals(b.Type, "httpTrigger", StringComparison.OrdinalIgnoreCase))))
                    {
                        Task<HttpResponseMessage> requestTask = client.PutAsJsonAsync($"/admin/functions/{function.Name}/keys/default?code={FunctionAppMasterKey}", new { name = "default", value = FunctionDefaultKey });
                        requests.Add(requestTask);
                    }

                    await Task.WhenAll(requests);

                    requests.ForEach(t => t.Result.EnsureSuccessStatusCode());
                }
            }
        }

        private async Task UpdateSiteContents()
        {
            Trace.WriteLine("Updating site contents...");
            Telemetry.TrackEvent("ContentUpdate");
            await _kuduClient.DeleteDirectory("site/wwwroot", true);
            string filePath = Path.ChangeExtension(Path.GetTempFileName(), "zip");
            ZipFile.CreateFromDirectory(Path.Combine(Environment.CurrentDirectory, "Functions"), filePath);

            await _kuduClient.UploadZip("site", filePath);
        }

        private async Task UpdateRuntime()
        {
            Trace.WriteLine($"Updating runtime from: {Settings.RuntimeExtensionPackageUrl}");
            Telemetry.TrackEvent("RuntimeUpdate", new Dictionary<string, string> { { "runtimeSource", Settings.RuntimeExtensionPackageUrl } });

            string extensionFilePath = Path.ChangeExtension(Path.GetTempFileName(), "zip");

            using (var client = new HttpClient())
            {
                HttpResponseMessage response = await client.GetAsync(Settings.RuntimeExtensionPackageUrl);
                response.EnsureSuccessStatusCode();


                using (var fileStream = File.OpenWrite(extensionFilePath))
                {
                    await response.Content.CopyToAsync(fileStream);
                }
            }

            await _kuduClient.DeleteDirectory("SiteExtensions", true);
            await _kuduClient.UploadZip("/", extensionFilePath);

            Trace.WriteLine($"Updated to function runtime version: {Settings.RuntimeVersion}");
        }

        public async Task RestartSite()
        {
            await IssueSiteCommand($"/subscriptions/{Settings.SiteSubscriptionId}/resourceGroups/{Settings.SiteResourceGroup}/providers/Microsoft.Web/sites/{Settings.SiteName}/restart?api-version=2015-08-01&softRestart=true&synchronous=true");
        }

        public async Task StopSite()
        {
            await IssueSiteCommand($"/subscriptions/{Settings.SiteSubscriptionId}/resourceGroups/{Settings.SiteResourceGroup}/providers/Microsoft.Web/sites/{Settings.SiteName}/stop?api-version=2015-08-01");
        }
        public async Task StartSite()
        {
            await IssueSiteCommand($"/subscriptions/{Settings.SiteSubscriptionId}/resourceGroups/{Settings.SiteResourceGroup}/providers/Microsoft.Web/sites/{Settings.SiteName}/start?api-version=2015-08-01");
        }

        private async Task IssueSiteCommand(string commandUri)
        {
            string token = await ArmAuthenticationHelpers.AcquireTokenBySPN(Settings.SiteTenantId, Settings.SiteApplicationId, Settings.SiteClientSecret);

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                client.BaseAddress = new Uri("https://management.azure.com/");


                using (var response = await client.PostAsync(commandUri, null))
                {
                    response.EnsureSuccessStatusCode();
                }
            }

            await Task.Delay(5000);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _kuduClient?.Dispose();
                    Telemetry.Flush();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }

    [CollectionDefinition(Constants.FunctionAppCollectionName)]
    public class FunctionAppCollection : ICollectionFixture<FunctionAppFixture>
    {
        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }

}
