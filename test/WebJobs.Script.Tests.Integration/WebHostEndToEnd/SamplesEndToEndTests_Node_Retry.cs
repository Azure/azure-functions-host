// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    public abstract class SamplesEndToEndTests_Node_RetryBase<TTestFixture> : IClassFixture<TTestFixture> where TTestFixture : EndToEndTestFixture
    {
        protected readonly TTestFixture _fixture;

        public SamplesEndToEndTests_Node_RetryBase(TTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task HttpTrigger_RetryFunctionJson_Get_Succeeds()
        {
            var response = await SamplesTestHelpers.InvokeHttpTrigger(_fixture, "HttpTrigger-RetryFunctionJson");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);
            Assert.Equal("retryCount: 4", body);
        }

        [Fact]
        public async Task HttpTrigger_RetryHostJson_Get_Succeeds()
        {
            var response = await SamplesTestHelpers.InvokeHttpTrigger(_fixture, "HttpTrigger-RetryHostJson");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);
            Assert.Equal("retryCount: 2", body);
        }
    }

    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class SamplesEndToEndTests_Node_Retry : SamplesEndToEndTests_Node_RetryBase<SamplesNodeRetryTestFixture>
    {
        public SamplesEndToEndTests_Node_Retry(SamplesNodeRetryTestFixture fixture) : base(fixture)
        {
        }
    }

    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class SamplesEndToEndTests_Node_Retry_DecoupledWorker : SamplesEndToEndTests_Node_RetryBase<SamplesNodeRetryTestFixture_DecoupledWorker>
    {
        public SamplesEndToEndTests_Node_Retry_DecoupledWorker(SamplesNodeRetryTestFixture_DecoupledWorker fixture) : base(fixture)
        {
        }
    }

    public class SamplesNodeRetryTestFixture : EndToEndTestFixture
    {
        static SamplesNodeRetryTestFixture()
        {
        }

        public SamplesNodeRetryTestFixture()
            : base(Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "sample", "NodeRetry"), "samples", RpcWorkerConstants.NodeLanguageWorkerName)
        {
        }

        public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
        {
            base.ConfigureScriptHost(webJobsBuilder);
        }
    }

    public class SamplesNodeRetryTestFixture_DecoupledWorker : SamplesNodeRetryTestFixture
    {
        static SamplesNodeRetryTestFixture_DecoupledWorker()
        {
        }

        public SamplesNodeRetryTestFixture_DecoupledWorker()
        {
        }

        public override void ConfigureWebHost(IServiceCollection services)
        {
            base.ConfigureWebHost(services);

            services.Configure<FunctionsHostingConfigOptions>(o => o.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "node"));
        }

        public override void ConfigureWebHost(IConfigurationBuilder configBuilder)
        {
            var inMemorySettings = new Dictionary<string, string>();
            inMemorySettings["languageWorkers:probingPaths:0"] = Path.GetFullPath("DecoupledWorkers");

            configBuilder.AddInMemoryCollection(inMemorySettings);
        }
    }
}