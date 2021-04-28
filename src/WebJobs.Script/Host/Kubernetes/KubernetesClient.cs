// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class KubernetesClient
    {
        private const string HttpLeaderEndpointKey = "HTTP_LEADER_ENDPOINT";
        private readonly HttpClient _httpClient;
        private readonly string _httpLeaderEndpoint;

        public KubernetesClient(IEnvironment environment, HttpClient httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            _httpLeaderEndpoint = environment.GetEnvironmentVariable(HttpLeaderEndpointKey);
        }

        public async Task<KubernetesLockHandle> GetLock(string lockName)
        {
            var request = new HttpRequestMessage()
            {
                Method = HttpMethod.Get,
                RequestUri = GetRequestUri($"?name={lockName}")
            };

            var response = await _httpClient.SendAsync(request);

            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<KubernetesLockHandle>(responseString);
        }

        public async Task<KubernetesLockHandle> TryAcquireLock (string lockId, string ownerId, TimeSpan lockPeriod, CancellationToken cancellationToken)
        {
            var lockHandle = new KubernetesLockHandle();
            var request = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = GetRequestUri($"/acquire?name={lockId}&owner={ownerId}&duration={lockPeriod.TotalSeconds}&renewDeadline=10"),
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                lockHandle.LockId = lockId;
                lockHandle.Owner = ownerId;
            }
            return lockHandle;
        }

        public async Task<HttpResponseMessage> ReleaseLock(string lockId, string ownerId)
        {
            var lockHandle = new KubernetesLockHandle();
            var request = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = GetRequestUri($"/release?name={lockId}&owner={ownerId}")
            };

            return await _httpClient.SendAsync(request);
        }

        public Uri GetRequestUri(string requestStem)
        {
            return new Uri($"{_httpLeaderEndpoint}/lock{requestStem}");
        }
    }
}
