// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class KubernetesClient
    {
        private const string HttpLeaderEndpointKey = "HTTP_LEADER_ENDPOINT";
        private readonly HttpClient _httpClient;
        private readonly string _httpLeaderEndpoint;

        public KubernetesClient()
        {
            _httpClient = new HttpClient();
            _httpLeaderEndpoint = Environment.GetEnvironmentVariable(HttpLeaderEndpointKey);
        }

        public async Task<KubernetesLockResponse> GetLock(string lockName)
        {
            var request = new HttpRequestMessage();
            request.Method = HttpMethod.Get;
            request.RequestUri = GetRequestUri($"?name={lockName}");
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var responseString = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<KubernetesLockResponse>(responseString);
            }
            return new KubernetesLockResponse() { Owner = null };
        }

        public async Task<KubernetesLockHandle> TryAcquireLock (string lockId, string ownerId, string lockPeriod)
        {
            var lockHandle = new KubernetesLockHandle();
            var request = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = GetRequestUri($"/acquire?name={lockId}&owner={ownerId}&period={lockPeriod}&renewDeadline=10"),
            };

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                lockHandle.LockId = lockId;
                lockHandle.OwnerId = ownerId;
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

        private Uri GetRequestUri(string requestStem)
        {
            return new Uri($"{_httpLeaderEndpoint}/lock{requestStem}");
        }
    }
}
