// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.WebJobs.Script.Tests;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    // Base class containing the test methods
    public abstract class SamplesEndToEndTests_Node_RetryBase<TTestFixture> : IClassFixture<TTestFixture>
        where TTestFixture : EndToEndTestFixture
    {
        private readonly ScriptSettingsManager _settingsManager;
        protected readonly TTestFixture _fixture;

        public SamplesEndToEndTests_Node_RetryBase(TTestFixture fixture)
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

    // First test class using the original fixture
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class SamplesEndToEndTests_Node_Retry : SamplesEndToEndTests_Node_RetryBase<SamplesNodeRetryTestFixture>
    {
        public SamplesEndToEndTests_Node_Retry(SamplesNodeRetryTestFixture fixture)
            : base(fixture)
        {
        }
    }

    // Second test class using the new fixture
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class SamplesEndToEndTests_Node_Retry2 : SamplesEndToEndTests_Node_RetryBase<SamplesNodeRetryTestFixture2>
    {
        public SamplesEndToEndTests_Node_Retry2(SamplesNodeRetryTestFixture2 fixture)
            : base(fixture)
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

    public class SamplesNodeRetryTestFixture2 : EndToEndTestFixture
    {
        static SamplesNodeRetryTestFixture2()
        {
        }

        public SamplesNodeRetryTestFixture2()
            : base(Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "sample", "NodeRetry"), "samples", RpcWorkerConstants.NodeLanguageWorkerName)
        {
        }

        public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
        {
            base.ConfigureScriptHost(webJobsBuilder);
        }

        public override void ConfigureWebHost(IServiceCollection services)
        {
            base.ConfigureWebHost(services);

            services.Configure<FunctionsHostingConfigOptions>(o => o.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "node"));
        }

        public override void ConfigureWebHost(IConfigurationBuilder configBuilder)
        {
            //    base.ConfigureWebHost(configBuilder);

            var inMemorySettings = new Dictionary<string, string>();
            inMemorySettings["languageWorkers:probingPaths:0"] = Path.GetFullPath("DecoupledWorkers");

            configBuilder.AddInMemoryCollection(inMemorySettings);
        }
    }
}