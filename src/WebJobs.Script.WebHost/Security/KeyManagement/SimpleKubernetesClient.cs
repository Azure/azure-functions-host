// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.IO;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class SimpleKubernetesClient : IKubernetesClient, IDisposable
    {
        private const string NamespaceFile = "/run/secrets/kubernetes.io/serviceaccount/namespace";
        private const string TokenFile = "/run/secrets/kubernetes.io/serviceaccount/token";
        private const string CaFile = "/run/secrets/kubernetes.io/serviceaccount/ca.crt";
        private const string KubernetesSecretsDir = "/run/secrets/functions-keys";
        private readonly HttpClient _httpClient;
        private readonly IEnvironment _environment;
        private Action _watchCallback;
        private bool _disposed;
        private AutoRecoveringFileSystemWatcher _fileWatcher;

        public SimpleKubernetesClient(IEnvironment environment) : this(environment, CreateHttpClient())
        { }

        // for testing
        internal SimpleKubernetesClient(IEnvironment environment, HttpClient client)
        {
            _httpClient = client;
            _environment = environment;
            Task.Run(() => RunWatcher());
        }

        private string KubernetesObjectName => _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsKubernetesSecretName);

        public bool IsWritable => !string.IsNullOrEmpty(KubernetesObjectName);

        public async Task<IDictionary<string, string>> GetSecrets()
        {
            if (string.IsNullOrEmpty(KubernetesObjectName) && FileUtility.DirectoryExists(KubernetesSecretsDir))
            {
                return await GetFromFiles(KubernetesSecretsDir);
            }
            else if (!string.IsNullOrEmpty(KubernetesObjectName))
            {
                return await GetFromApiServer(KubernetesObjectName);
            }
            else
            {
                throw new InvalidOperationException($"{nameof(KubernetesSecretsRepository)} requires setting {EnvironmentSettingNames.AzureWebJobsKubernetesSecretName} or mounting secrets to {KubernetesSecretsDir}");
            }
        }

        public async Task UpdateSecrets(IDictionary<string, string> data)
        {
            (var url, var isSecret) = await GetObjectUrl(KubernetesObjectName);
            await CreateIfDoesntExist(url, isSecret);

            data = isSecret
                ? data.ToDictionary(k => k.Key, v => Convert.ToBase64String(Encoding.UTF8.GetBytes(v.Value)))
                : data;

            using (var request = await GetRequest(HttpMethod.Patch, url, new[] { new { op = "replace", path = "/data", value = data } }, "application/json-patch+json"))
            {
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
            }
        }

        public void OnSecretChange(Action callback)
        {
            _watchCallback = callback;
        }

        private async Task RunWatcher()
        {
            if (string.IsNullOrEmpty(KubernetesObjectName) && FileUtility.DirectoryExists(KubernetesSecretsDir))
            {
                _fileWatcher = new AutoRecoveringFileSystemWatcher(KubernetesSecretsDir);
                _fileWatcher.Changed += (object sender, FileSystemEventArgs e)
                    => _watchCallback?.Invoke();
            }
            else if (!string.IsNullOrEmpty(KubernetesObjectName))
            {
                (var url, _) = await GetObjectUrl(KubernetesObjectName, watchUrl: true);
                using (var noTimeoutClient = CreateHttpClient())
                using (var request = await GetRequest(HttpMethod.Get, url))
                {
                    while (!_disposed)
                    {
                        noTimeoutClient.Timeout = TimeSpan.FromMilliseconds(Timeout.Infinite);
                        using (var response = await noTimeoutClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                        using (var reader = new StreamReader(await response.Content.ReadAsStreamAsync()))
                        {
                            while (!reader.EndOfStream && !_disposed)
                            {
                                reader.ReadLine(); // Read the line-json update
                                _watchCallback?.Invoke();
                            }
                        }
                        await Task.Delay(TimeSpan.FromSeconds(1));
                    }
                }
            }
        }

        private async Task<IDictionary<string, string>> GetFromApiServer(string objectName)
        {
            (var url, var decode) = await GetObjectUrl(objectName);
            using (var request = await GetRequest(HttpMethod.Get, url))
            {
                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var obj = await response.Content.ReadAsAsync<JObject>();
                    return obj["data"]
                        ?.ToObject<IDictionary<string, string>>()
                        ?.ToDictionary(
                            k => k.Key,
                            v => decode
                                ? Encoding.UTF8.GetString(Convert.FromBase64String(v.Value))
                                : v.Value);
                }
                else if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    // If the collection is not there, return an empty list
                    return new Dictionary<string, string>();
                }
                else
                {
                    throw new HttpRequestException($"Error calling GET {url}, Status: {response.StatusCode}, Content: {await response.Content.ReadAsStringAsync()}");
                }
            }
        }

        private async Task CreateIfDoesntExist(string url, bool isSecret)
        {
            using (var request = await GetRequest(HttpMethod.Get, url))
            {
                var response = await _httpClient.SendAsync(request);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    var name = url.Split("/").Last();
                    url = url.Substring(0, url.LastIndexOf("/"));
                    var payload = new
                    {
                        kind = isSecret ? "Secret" : "ConfigMap",
                        apiVersion = "v1",
                        metadata = new
                        {
                            name = name
                        }
                    };
                    using (var createRequest = await GetRequest(HttpMethod.Post, url, payload))
                    {
                        var createResponse = await _httpClient.SendAsync(createRequest);
                        createResponse.EnsureSuccessStatusCode();
                    }
                }
            }
        }

        private async Task<(string, bool)> GetObjectUrl(string objectName, bool watchUrl = false)
        {
            var isSecret = true;
            if (!objectName.StartsWith("secrets/") && !objectName.StartsWith("configmaps/"))
            {
                // assume a secret if no type is specified.
                objectName = $"secrets/{objectName}";
            }
            else if (objectName.StartsWith("configmaps/"))
            {
                // This allows the users to manage their keys in configMaps instead of secret.
                // There is no difference currently in kubernetes between the 2 other than secrets
                // requiring base64 encoding. If it's a configMap, then we don't need to decode it.
                isSecret = false;
            }

            string @namespace = await FileUtility.ReadAsync(NamespaceFile);
            var url = $"{_environment.GetKubernetesApiServerUrl()}/api/v1/";
            if (watchUrl)
            {
                url += "watch/";
            }
            url += $"namespaces/{@namespace}/{objectName}";
            return (url, isSecret);
        }

        private async Task<HttpRequestMessage> GetRequest(HttpMethod method, string url, object payload = null, string contentType = null, bool closeConnection = true)
        {
            string token = await FileUtility.ReadAsync(TokenFile);
            const string jsonContentType = "application/json";
            contentType = contentType ?? jsonContentType;

            var request = new HttpRequestMessage(method, url);
            request.Headers.Add(HeaderNames.Authorization, $"Bearer {token}");
            request.Headers.Add(HeaderNames.Accept, jsonContentType);

            if (closeConnection)
            {
                request.Headers.Add(HeaderNames.Connection, "close");
            }

            if (payload != null)
            {
                var jsonPayload = JsonConvert.SerializeObject(payload);
                request.Content = new StringContent(jsonPayload, Encoding.UTF8, contentType);
            }

            return request;
        }

        private static async Task<IDictionary<string, string>> GetFromFiles(string path)
        {
            string[] files = await FileUtility.GetFilesAsync(path, "*");
            var secrets = new Dictionary<string, string>(files.Length);
            foreach (var file in files)
            {
                secrets.Add(Path.GetFileName(file), await FileUtility.ReadAsync(file));
            }
            return secrets;
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = ServerCertificateValidationCallback
            });

            client.DefaultRequestHeaders.Add(HeaderNames.UserAgent, ScriptConstants.HostUserAgent);
            return client;
        }

        private static bool ServerCertificateValidationCallback(
            HttpRequestMessage request,
            X509Certificate2 certificate,
            X509Chain certChain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                // certificate is already valid
                return true;
            }
            else if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateNameMismatch ||
                sslPolicyErrors == SslPolicyErrors.RemoteCertificateNotAvailable)
            {
                // api-server cert must exist and have the right subject
                return false;
            }
            else if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors)
            {
                // only remaining error state is RemoteCertificateChainErrors
                // check custom CA
                var privateChain = new X509Chain();
                privateChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

                var caCert = new X509Certificate2(CaFile);
                // https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chainpolicy?view=netcore-2.2
                // Add CA cert to the chain store to include it in the chain check.
                privateChain.ChainPolicy.ExtraStore.Add(caCert);
                // Build the chain for `certificate` which should be the self-signed kubernetes api-server cert.
                privateChain.Build(certificate);

                foreach (X509ChainStatus chainStatus in privateChain.ChainStatus)
                {
                    if (chainStatus.Status != X509ChainStatusFlags.NoError &&
                        // root CA cert is not always trusted.
                        chainStatus.Status != X509ChainStatusFlags.UntrustedRoot)
                    {
                        return false;
                    }
                }

                return true;
            }
            else
            {
                // Unknown sslPolicyErrors
                return false;
            }
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _httpClient.Dispose();
                    _fileWatcher?.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}