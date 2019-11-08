// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class MeshInitServiceClient : IMeshInitServiceClient
    {
        private readonly HttpClient _client;
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;

        public MeshInitServiceClient(HttpClient client, IEnvironment environment,
            ILogger<MeshInitServiceClient> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        public async Task MountCifs(string connectionString, string contentShare, string targetPath)
        {
            var sa = CloudStorageAccount.Parse(connectionString);
            var key = Convert.ToBase64String(sa.Credentials.ExportKey());
            await Mount(new[]
            {
                new KeyValuePair<string, string>("operation", "cifs"),
                new KeyValuePair<string, string>("host", sa.FileEndpoint.Host),
                new KeyValuePair<string, string>("accountName", sa.Credentials.AccountName),
                new KeyValuePair<string, string>("accountKey", key),
                new KeyValuePair<string, string>("contentShare", contentShare),
                new KeyValuePair<string, string>("targetPath", targetPath),
            });
        }

        public Task MountBlob(string connectionString, string contentShare, string targetPath)
        {
            // todo: Implement once mesh init server supports mounting blobs
            throw new NotImplementedException();
        }

        public async Task MountFuse(string type, string filePath, string scriptPath)
            => await Mount(new[]
            {
                new KeyValuePair<string, string>("operation", type),
                new KeyValuePair<string, string>("filePath", filePath),
                new KeyValuePair<string, string>("targetPath", scriptPath),
            });

        private async Task Mount(IEnumerable<KeyValuePair<string, string>> formData)
        {
            var res = await _client.PostAsync(_environment.GetEnvironmentVariable(EnvironmentSettingNames.MeshInitURI), new FormUrlEncodedContent(formData));
            _logger.LogInformation("Response {res} from init", res);
        }
    }
}