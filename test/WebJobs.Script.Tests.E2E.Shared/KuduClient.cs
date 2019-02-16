// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WebJobs.Script.Tests.EndToEnd.Shared
{
    public class KuduClient : IDisposable
    {
        private readonly Uri _uri;
        private readonly string _userName;
        private readonly string _password;
        private readonly HttpClient _client;
        private bool _disposed;

        public KuduClient(string kuduUri, string userName, string password)
        {
            _uri = new Uri(kuduUri);
            _userName = userName;
            _password = password;

            _client = new HttpClient();
            var byteArray = Encoding.ASCII.GetBytes($"{userName}:{password}");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
            _client.BaseAddress = _uri;
        }

        public async Task DeleteDirectory(string path, bool recursive)
        {
            HttpResponseMessage response = await _client.DeleteAsync($"api/vfs/{path}/?recursive={recursive}");
            // response.EnsureSuccessStatusCode();
        }

        public async Task DeployZip(string zipPath)
        {
            var content = new StreamContent(File.Open(zipPath, FileMode.Open));
            HttpResponseMessage response = await _client.PostAsync("/api/zipdeploy", content);
            response.EnsureSuccessStatusCode();
        }

        public async Task UploadZip(string sitePath, string zipPath)
        {
            var content = new StreamContent(File.Open(zipPath, FileMode.Open));
            await _client.PutAsync($"/api/zip/{sitePath}/", content);
        }

        public async Task<List<Function>> GetFunctions()
        {
            HttpResponseMessage response = await _client.GetAsync("api/functions");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsAsync<List<Function>>();
        }

        public async Task<string> GetFunctionsMasterKey()
        {
            HttpResponseMessage response = await _client.GetAsync("api/functions/admin/masterkey");
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadAsAsync<JObject>();

            return result.GetValue("masterKey").ToString();
        }

        public async Task SetAppSetting(string name, string value)
        {
            var httpContent = new StringContent($"{{ {name} : {value} }}", Encoding.UTF8, "application/json");
            await _client.PostAsync($"/api/settings", httpContent);
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => _client.SendAsync(request);

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _client?.Dispose();
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
