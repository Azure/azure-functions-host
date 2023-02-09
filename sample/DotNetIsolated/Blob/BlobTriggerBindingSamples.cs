// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace SampleApp
{
    public class BlobTriggerBindingSamples
    {
        private readonly ILogger<BlobTriggerBindingSamples> _logger;

        public BlobTriggerBindingSamples(ILogger<BlobTriggerBindingSamples> logger)
        {
            _logger = logger;
        }

        [Function(nameof(BlobClientFunction))]
        public async Task BlobClientFunction(
            [BlobTrigger("test-input-client/{name}")] BlobClient client, string name)
        {
            var downloadResult = await client.DownloadContentAsync();
            var content = downloadResult.Value.Content.ToString();
            _logger.LogInformation($"{name} - {content}");
        }

        [Function(nameof(BlobStreamFunction))]
        public async Task BlobStreamFunction(
            [BlobTrigger("test-input-stream/{name}")] Stream stream, string name)
        {
            using var blobStreamReader = new StreamReader(stream);
            var content = await blobStreamReader.ReadToEndAsync();
            _logger.LogInformation($"{name} - {content}", name, content);
        }

        [Function(nameof(BlobByteArrayFunction))]
        public void BlobByteArrayFunction(
            [BlobTrigger("test-input-byte")] Byte[] data)
        {
            _logger.LogInformation(Encoding.Default.GetString(data));
        }

        [Function(nameof(BlobStringFunction))]
        public void BlobStringFunction(
            [BlobTrigger("test-input-string")] string data)
        {
            _logger.LogInformation(data);
        }

        [Function(nameof(BlobBookFunction))]
        public void BlobBookFunction(
            [BlobTrigger("test-input-book")] Book data)
        {
            _logger.LogInformation($"{data.Id} - {data.Name}");
        }
    }
}
