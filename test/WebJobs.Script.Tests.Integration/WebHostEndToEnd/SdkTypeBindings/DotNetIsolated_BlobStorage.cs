// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.Queue;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SdkTypeBindingEndToEnd)]
    [Collection("Sequential")]
    public class DotNetIsolated_BlobStorage : EndToEndTestsBase<DotNetIsolated_BlobStorage.TestFixture>, IAsyncDisposable
    {
        public DotNetIsolated_BlobStorage(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task QueueTrigger_BlobInput_ExpressionFunction_Invoke_Succeeds()
        {
            var inputText = "This book is titled 'Golden Compass'";
            var inputBook = @"{ ""id"": ""1"", ""name"": ""Golden Compass"" }";

            CloudBlockBlob testBlob = Fixture.TestInputContainer.GetBlockBlobReference("1.txt");
            await testBlob.UploadTextAsync(inputText);

            var message = new CloudQueueMessage(inputBook);
            await Fixture.TestQueue.AddMessageAsync(message);

            var logs = await GetFunctionLogs("ExpressionFunction");

            Assert.True(logs.Any(p => p.FormattedMessage.Contains(inputText)));
        }

        [Fact]
        public async Task HttpTrigger_BlobInputClientFunction_Invoke_Succeeds()
        {
            var inputText = "BlobInputClientFunction - hello world";

            CloudBlockBlob testBlob = Fixture.TestInputContainer.GetBlockBlobReference("sample1.txt");
            await testBlob.UploadTextAsync(inputText);

            var response = await SamplesTestHelpers.InvokeHttpTrigger(Fixture, "BlobInputClientFunction");
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(inputText, responseContent);
        }

        [Fact]
        public async Task HttpTrigger_BlobInputStreamFunction_Invoke_Succeeds()
        {
            var inputText = "BlobInputStreamFunction - hello world";

            CloudBlockBlob testBlob = Fixture.TestInputContainer.GetBlockBlobReference("sample1.txt");
            await testBlob.UploadTextAsync(inputText);

            var response = await SamplesTestHelpers.InvokeHttpTrigger(Fixture, "BlobInputStreamFunction");
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(inputText, responseContent);
        }

        [Fact]
        public async Task HttpTrigger_BlobInputByteArrayFunction_Invoke_Succeeds()
        {
            var inputText = "BlobInputByteArrayFunction - hello world";

            CloudBlockBlob testBlob = Fixture.TestInputContainer.GetBlockBlobReference("sample1.txt");
            await testBlob.UploadTextAsync(inputText);

            var response = await SamplesTestHelpers.InvokeHttpTrigger(Fixture, "BlobInputByteArrayFunction");
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(inputText, responseContent);
        }

        [Fact]
        public async Task HttpTrigger_BlobInputStringFunction_Invoke_Succeeds()
        {
            var inputText = "BlobInputStringFunction - hello world";

            CloudBlockBlob testBlob = Fixture.TestInputContainer.GetBlockBlobReference("sample1.txt");
            await testBlob.UploadTextAsync(inputText);

            var response = await SamplesTestHelpers.InvokeHttpTrigger(Fixture, "BlobInputStringFunction");
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(inputText, responseContent);
        }

        [Fact]
        public async Task HttpTrigger_BlobInputBookFunction_Invoke_Succeeds()
        {
            var inputBook = @"{ ""id"": ""1"", ""name"": ""Golden Compass"" }";

            CloudBlockBlob testBlob = Fixture.TestInputContainer.GetBlockBlobReference("book.json");
            await testBlob.UploadTextAsync(inputBook);

            var response = await SamplesTestHelpers.InvokeHttpTrigger(Fixture, "BlobInputBookFunction");
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Golden Compass", responseContent);
        }

        [Fact]
        public async Task BlobTrigger_BlobClientFunction_Invoke_Succeeds()
        {
            var inputText = "BlobClientFunction - hello world";

            var inputContainer = Fixture.BlobClient.GetContainerReference("test-input-client");
            await inputContainer.CreateIfNotExistsAsync();

            CloudBlockBlob testBlob = inputContainer.GetBlockBlobReference("sample1.txt");
            await testBlob.UploadTextAsync(inputText);

            var logs = await GetFunctionLogs("BlobClientFunction");
            Assert.True(logs.Any(p => p.FormattedMessage.Contains(inputText)));

            await TestHelpers.ClearContainerAsync(inputContainer);
        }

        [Fact]
        public async Task BlobTrigger_BlobStreamFunction_Invoke_Succeeds()
        {
            var inputText = "BlobStreamFunction - hello world";

            var inputContainer = Fixture.BlobClient.GetContainerReference("test-input-stream");
            await inputContainer.CreateIfNotExistsAsync();

            CloudBlockBlob testBlob = inputContainer.GetBlockBlobReference("sample1.txt");
            await testBlob.UploadTextAsync(inputText);

            var logs = await GetFunctionLogs("BlobStreamFunction");
            Assert.True(logs.Any(p => p.FormattedMessage.Contains(inputText)));

            await TestHelpers.ClearContainerAsync(inputContainer);
        }

        [Fact]
        public async Task BlobTrigger_BlobByteArrayFunction_Invoke_Succeeds()
        {
            var inputText = "BlobByteArrayFunction - hello world";

            var inputContainer = Fixture.BlobClient.GetContainerReference("test-input-byte");
            await inputContainer.CreateIfNotExistsAsync();

            CloudBlockBlob testBlob = inputContainer.GetBlockBlobReference("sample1.txt");
            await testBlob.UploadTextAsync(inputText);

            var logs = await GetFunctionLogs("BlobByteArrayFunction");
            Assert.True(logs.Any(p => p.FormattedMessage.Contains(inputText)));

            await TestHelpers.ClearContainerAsync(inputContainer);
        }

        [Fact]
        public async Task BlobTrigger_BlobStringFunction_Invoke_Succeeds()
        {
            var inputText = "BlobStringFunction - hello world";

            var inputContainer = Fixture.BlobClient.GetContainerReference("test-input-string");
            await inputContainer.CreateIfNotExistsAsync();

            CloudBlockBlob testBlob = inputContainer.GetBlockBlobReference("sample1.txt");
            await testBlob.UploadTextAsync(inputText);

            var logs = await GetFunctionLogs("BlobStringFunction");
            Assert.True(logs.Any(p => p.FormattedMessage.Contains(inputText)));

            await TestHelpers.ClearContainerAsync(inputContainer);
        }

        [Fact]
        public async Task BlobTrigger_BlobBookFunction_Invoke_Succeeds()
        {
            var inputBook = @"{ ""id"": ""1"", ""name"": ""Golden Compass"" }";

            var inputContainer = Fixture.BlobClient.GetContainerReference("test-input-book");
            await inputContainer.CreateIfNotExistsAsync();

            CloudBlockBlob testBlob = inputContainer.GetBlockBlobReference("book.json");
            await testBlob.UploadTextAsync(inputBook);

            var logs = await GetFunctionLogs("BlobBookFunction");
            Assert.True(logs.Any(p => p.FormattedMessage.Contains("1 - Golden Compass")));

            await TestHelpers.ClearContainerAsync(inputContainer);
        }

        public async ValueTask DisposeAsync()
        {
            await TestHelpers.ClearContainerAsync(Fixture.TestInputContainer);
        }

        public class TestFixture : EndToEndTestFixture
        {
            private static string rootPath = Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "..", "sample", "DotNetIsolated", "bin", "Debug", "net6.0");

            public TestFixture() : base(rootPath, "sample", RpcWorkerConstants.DotNetIsolatedLanguageWorkerName, setStorageEnvironmentVariable: true)
            {
            }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureScriptHost(webJobsBuilder);

                webJobsBuilder.AddAzureStorage()
                    .Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = new[]
                    {
                        "BlobInputClientFunction",
                        "BlobInputStreamFunction",
                        "BlobInputByteArrayFunction",
                        "BlobInputStringFunction",
                        "BlobInputBookFunction",
                        "BlobClientFunction",
                        "BlobStreamFunction",
                        "BlobByteArrayFunction",
                        "BlobStringFunction",
                        "BlobBookFunction",
                        "ExpressionFunction"
                    };
                });
            }
        }
    }
}