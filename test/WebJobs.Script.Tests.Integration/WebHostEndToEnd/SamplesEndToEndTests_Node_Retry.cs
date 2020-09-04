// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.Azure.Storage.Blob;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class SamplesEndToEndTests_Node_Retry : IClassFixture<SamplesEndToEndTests_Node_Retry.TestFixture>
    {
        private readonly ScriptSettingsManager _settingsManager;
        private TestFixture _fixture;

        public SamplesEndToEndTests_Node_Retry(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
        }

        [Fact]
        public async Task HttpTrigger_RetryFunctionJson_Get_Succeeds()
        {
            var response = await SamplesTestHelpers.InvokeHttpTrigger(_fixture, "HttpTrigger-RetryFunctionJson");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);
            Assert.Equal("invocationCount: 4", body);
        }

        [Fact]
        public async Task HttpTrigger_RetryHostJson_Get_Succeeds()
        {
            var response = await SamplesTestHelpers.InvokeHttpTrigger(_fixture, "HttpTrigger-RetryHostJson");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);
            Assert.Equal("invocationCount: 2", body);
        }

        public class TestFixture : EndToEndTestFixture
        {
            static TestFixture()
            {
            }
            
            public TestFixture()
                : base(Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\sample\NodeRetry"), "samples", RpcWorkerConstants.NodeLanguageWorkerName)
            {
            }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureScriptHost(webJobsBuilder);
            }
        }
    }
}