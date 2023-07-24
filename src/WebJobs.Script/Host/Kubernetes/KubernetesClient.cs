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
        private const int LeaseRenewDeadline = 10;
        private readonly HttpClient _httpClient;
        private readonly string _httpLeaderEndpoint;

        internal KubernetesClient(IEnvironment environment, HttpClient httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            _httpLeaderEndpoint = environment.GetHttpLeaderEndpoint();
        }

        internal async Task<KubernetesLockHandle> GetLock(string lockName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(lockName))
            {
                throw new ArgumentNullException(nameof(lockName));
            }

            var request = new HttpRequestMessage()
            {
                Method = HttpMethod.Get,
                RequestUri = GetRequestUri($"?name={lockName}")
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);

            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<KubernetesLockHandle>(responseString);
        }

        internal async Task<KubernetesLockHandle> TryAcquireLock(string lockId, string ownerId, TimeSpan lockPeriod, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(lockId))
            {
                throw new ArgumentNullException(nameof(lockId));
            }

            if (string.IsNullOrEmpty(ownerId))
            {
                throw new ArgumentNullException(nameof(ownerId));
            }

            var lockHandle = new KubernetesLockHandle();
            var request = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = GetRequestUri($"/acquire?name={lockId}&owner={ownerId}&duration={lockPeriod.TotalSeconds}&renewDeadline={LeaseRenewDeadline}"),
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                lockHandle.LockId = lockId;
                lockHandle.Owner = ownerId;
                lockHandle.LockPeriod = lockPeriod.TotalSeconds.ToString();
            }
            return lockHandle;
        }

        internal async Task<HttpResponseMessage> ReleaseLock(string lockId, string ownerId)
        {
            if (string.IsNullOrEmpty(lockId))
            {
                throw new ArgumentNullException(nameof(lockId));
            }

            var request = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = GetRequestUri($"/release?name={lockId}&owner={ownerId}")
            };

            return await _httpClient.SendAsync(request);
        }

        internal Uri GetRequestUri(string requestStem)
        {
            return new Uri($"{_httpLeaderEndpoint}/lock{requestStem}");
        }
    }
}
