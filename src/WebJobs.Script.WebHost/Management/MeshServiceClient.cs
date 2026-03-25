// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Storage;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class MeshServiceClient : IMeshServiceClient
    {
        private const string Operation = "operation";
        private const string BindMountOperation = "bind-mount";
        public const string SquashFsOperation = "squashfs";
        public const string ZipOperation = "zip";
        public const string AddFES = "add-fes";
        private readonly HttpClient _client;
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;

        private static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings());

        public MeshServiceClient(IHttpClientFactory httpClientFactory, IEnvironment environment, ILogger<MeshServiceClient> logger)
        {
            _client = httpClientFactory?.CreateClient() ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        public async Task<bool> MountCifs(string connectionString, string contentShare, string targetPath)
        {
            var sa = CloudStorageAccount.Parse(connectionString);
            var key = Convert.ToBase64String(sa.Credentials.ExportKey());

            HttpResponseMessage responseMessage = await SendAsync(new[]
            {
                new KeyValuePair<string, string>(Operation, "cifs"),
                new KeyValuePair<string, string>("host", sa.FileEndpoint.Host),
                new KeyValuePair<string, string>("accountName", sa.Credentials.AccountName),
                new KeyValuePair<string, string>("accountKey", key),
                new KeyValuePair<string, string>("contentShare", contentShare),
                new KeyValuePair<string, string>("targetPath", targetPath),
            });

            return responseMessage.IsSuccessStatusCode;
        }

        public Task MountBlob(string connectionString, string contentShare, string targetPath)
        {
            // todo: Implement once mesh init server supports mounting blobs
            throw new NotImplementedException(nameof(MountBlob));
        }

        public async Task MountFuse(string type, string filePath, string scriptPath)
        {
            _logger.LogDebug($"Creating {type} mount from {filePath} to {scriptPath}");

            await SendAsync(new[]
            {
                new KeyValuePair<string, string>(Operation, type),
                new KeyValuePair<string, string>("filePath", filePath),
                new KeyValuePair<string, string>("targetPath", scriptPath),
            });
        }

        public async Task PublishContainerActivity(IEnumerable<ContainerFunctionExecutionActivity> activities)
        {
            _logger.LogDebug($"Publishing {activities.Count()} container activities");

            try
            {
                await Utility.InvokeWithRetriesAsync(async () =>
                {
                    await PublishActivities(activities);
                }, 2, TimeSpan.FromSeconds(0.5));
            }
            catch (Exception e)
            {
                _logger.LogError(e, nameof(PublishContainerActivity));
            }
        }

        public async Task NotifyHealthEvent(ContainerHealthEventType healthEventType, Type source, string details)
        {
            var healthEvent = new ContainerHealthEvent()
            {
                EventTime = DateTime.UtcNow,
                EventType = healthEventType,
                Details = details,
                Source = source.ToString()
            };

            var healthEventString = Serialize(healthEvent);

            _logger.LogInformation($"Posting health event {healthEventString}");

            var responseMessage = await SendAsync(new[]
            {
                new KeyValuePair<string, string>(Operation, "add-health-event"),
                new KeyValuePair<string, string>("healthEvent", healthEventString),
            });

            _logger.LogInformation($"Posted health event status: {responseMessage.StatusCode}");
        }

        public async Task CreateBindMount(string sourcePath, string targetPath)
        {
            _logger.LogDebug($"Creating bind mount from {sourcePath} to {targetPath}");

            var httpResponseMessage = await SendAsync(new[]
            {
                new KeyValuePair<string, string>(Operation, BindMountOperation),
                new KeyValuePair<string, string>("sourcePath", sourcePath),
                new KeyValuePair<string, string>("targetPath", targetPath),
            });

            httpResponseMessage.EnsureSuccessStatusCode();
        }

        private async Task PublishActivities(IEnumerable<ContainerFunctionExecutionActivity> activities)
        {
            // Log one of the activities being published for debugging.
            _logger.LogDebug($"Publishing function execution activity {activities.FirstOrDefault()}");

            var operation = new[]
            {
                new KeyValuePair<string, string>(Operation, AddFES),
                new KeyValuePair<string, string>("content", Serialize(activities)),
            };

            var response = await SendAsync(operation);
            response.EnsureSuccessStatusCode();
        }

        private async Task<HttpResponseMessage> SendAsync(IEnumerable<KeyValuePair<string, string>> formData)
        {
            var operationName = formData.FirstOrDefault(f => string.Equals(f.Key, Operation)).Value;
            _logger.LogDebug($"Sending mesh request {operationName}");

            var request = new HttpRequestMessage(HttpMethod.Post, _environment.GetEnvironmentVariable(EnvironmentSettingNames.MeshInitURI))
            {
                Content = new FormUrlEncodedContent(formData)
            };

            request.Headers.Add(ScriptConstants.ContainerInstanceHeader, _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName));

            var res = await _client.SendAsync(request);

            _logger.LogDebug($"Mesh response {res.StatusCode}");

            return res;
        }

        private static string Serialize<T>(T o)
        {
            string serialized;
            using (var stringWriter = new StringWriter())
            {
                Serializer.Serialize(stringWriter, o);
                serialized = stringWriter.ToString();
            }

            return serialized;
        }
    }
}