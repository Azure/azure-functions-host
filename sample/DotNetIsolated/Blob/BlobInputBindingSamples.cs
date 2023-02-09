// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace SampleApp
{
    public class BlobInputBindingSamples
    {
        private readonly ILogger<BlobInputBindingSamples> _logger;

        public BlobInputBindingSamples(ILogger<BlobInputBindingSamples> logger)
        {
            _logger = logger;
        }

        [Function(nameof(BlobInputClientFunction))]
        public async Task<HttpResponseData> BlobInputClientFunction(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req,
            [BlobInput("test-input-sample/sample1.txt")] BlobClient client)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            var downloadResult = await client.DownloadContentAsync();
            await response.Body.WriteAsync(downloadResult.Value.Content);
            return response;
        }

        [Function(nameof(BlobInputStreamFunction))]
        public async Task<HttpResponseData> BlobInputStreamFunction(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req,
            [BlobInput("test-input-sample/sample1.txt")] Stream stream)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            using var blobStreamReader = new StreamReader(stream);
            await response.WriteStringAsync(blobStreamReader.ReadToEnd());
            return response;
        }

        [Function(nameof(BlobInputByteArrayFunction))]
        public HttpResponseData BlobInputByteArrayFunction(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req,
            [BlobInput("test-input-sample/sample1.txt")] Byte[] data)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync(Encoding.Default.GetString(data));
            return response;
        }

        [Function(nameof(BlobInputStringFunction))]
        public HttpResponseData BlobInputStringFunction(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req,
            [BlobInput("test-input-sample/sample1.txt")] string data)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync(data);
            return response;
        }

        [Function(nameof(BlobInputBookFunction))]
        public HttpResponseData BlobInputBookFunction(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req,
            [BlobInput("test-input-sample/book.json")] Book data)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync(data.Name);
            return response;
        }

        [Function(nameof(BlobInputCollectionFunction))]
        public HttpResponseData BlobInputCollectionFunction(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req,
            [BlobInput("test-input-sample", IsBatched = true)] IEnumerable<BlobClient> blobs)
        {
            List<string> blobList = new();

            foreach (BlobClient blob in blobs)
            {
                _logger.LogInformation("Blob name: {blobName}, Container name: {containerName}", blob.Name, blob.BlobContainerName);
                blobList.Add(blob.Name);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync(blobList);
            return response;
        }

        [Function(nameof(BlobInputStringArrayFunction))]
        public HttpResponseData BlobInputStringArrayFunction(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req,
            [BlobInput("test-input-sample", IsBatched = true)] string[] blobContent)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync(blobContent);
            return response;
        }

        [Function(nameof(BlobInputBookArrayFunction))]
        public HttpResponseData BlobInputBookArrayFunction(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req,
            [BlobInput("test-input-sample", IsBatched = true)] Book[] books)
        {
            List<string> bookNames = new();

            foreach (var item in books)
            {
                bookNames.Add(item.Name);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync(bookNames);
            return response;
        }
    }
}