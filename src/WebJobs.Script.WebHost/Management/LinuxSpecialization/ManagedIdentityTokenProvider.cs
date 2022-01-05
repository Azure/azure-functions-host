// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization
{
    public class ManagedIdentityTokenProvider : IManagedIdentityTokenProvider
    {
        private const string ApiVersion = "1.0";

        private readonly IEnvironment _environment;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMetricsLogger _metricsLogger;
        private readonly ILogger<ManagedIdentityTokenProvider> _logger;

        public ManagedIdentityTokenProvider(IEnvironment environment, IHttpClientFactory httpClientFactory,
            IMetricsLogger metricsLogger, ILogger<ManagedIdentityTokenProvider> logger)
        {
            _environment = environment;
            _metricsLogger = metricsLogger;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        private string GetRunFromPackageIdentity()
        {
            var runFromPackageIdentity = _environment.GetEnvironmentVariable(EnvironmentSettingNames.RunFromPackageManagedResourceId);
            if (string.IsNullOrEmpty(runFromPackageIdentity))
            {
                _logger.LogDebug(
                    "No '{EnvironmentSettingNames.RunFromPackageManagedResourceId}' specified. Falling back to using '{EnvironmentSettingNames.SystemAssignedManagedIdentity}'",
                    EnvironmentSettingNames.RunFromPackageManagedResourceId,
                    EnvironmentSettingNames.SystemAssignedManagedIdentity);
                return string.Empty;
            }

            if (string.Equals(EnvironmentSettingNames.SystemAssignedManagedIdentity, runFromPackageIdentity, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Using '{EnvironmentSettingNames.SystemAssignedManagedIdentity}' to download package", EnvironmentSettingNames.SystemAssignedManagedIdentity);
                // returning empty string defaults to system assigned identity
                return string.Empty;
            }

            _logger.LogDebug("Using Managed ResourceId '{runFromPackageIdentity}' to download package", runFromPackageIdentity);
            return runFromPackageIdentity;
        }

        private string GetTokenEndpoint(string resourceUrl)
        {
            if (!Utility.TryGetUriHost(resourceUrl, out var resourceHost))
            {
                throw new ArgumentException(nameof(resourceUrl));
            }

            var msiEndpoint = _environment.GetEnvironmentVariable(EnvironmentSettingNames.MsiEndpoint);
            if (string.IsNullOrEmpty(msiEndpoint))
            {
                throw new InvalidOperationException("MSI is not enabled on the app. Failed to acquire ManagedIdentity token");
            }

            var tokenEndpoint =
                $"{msiEndpoint}?api-version={ApiVersion}&resource={resourceHost}";

            var resourceId = GetRunFromPackageIdentity();
            if (!string.IsNullOrEmpty(resourceId))
            {
                tokenEndpoint = string.Concat(tokenEndpoint, $"&mi_res_id={resourceId}");
            }

            return tokenEndpoint;
        }

        public async Task<string> GetManagedIdentityToken(string resourceUrl)
        {
            var tokenEndpoint = GetTokenEndpoint(resourceUrl);
            return await GetTokenWithRetries(tokenEndpoint);
        }

        private async Task<string> GetTokenWithRetries(string tokenEndpoint)
        {
            string token = null;
            await Utility.InvokeWithRetriesAsync(async () =>
            {
                token = await GetToken(tokenEndpoint);
            }, maxRetries: 2, TimeSpan.FromMilliseconds(100));

            return token;
        }

        private async Task<string> GetToken(string tokenEndpoint)
        {
            try
            {
                using (_metricsLogger.LatencyEvent(MetricEventNames.LinuxContainerSpecializationFetchMIToken))
                {
                    using (var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, tokenEndpoint))
                    {
                        var httpClient = _httpClientFactory.CreateClient();
                        var msiToken = _environment.GetEnvironmentVariable(EnvironmentSettingNames.MsiSecret);
                        httpRequestMessage.Headers.Add(ScriptConstants.XIdentityHeader, msiToken);
                        using (var response = await httpClient.SendAsync(httpRequestMessage))
                        {
                            response.EnsureSuccessStatusCode();
                            var readAsStringAsync = await response.Content.ReadAsStringAsync();
                            var msiResponse = JsonConvert.DeserializeObject<TokenServiceMsiResponse>(readAsStringAsync);
                            return msiResponse.AccessToken;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, nameof(GetToken));
                throw;
            }
        }
    }
}
