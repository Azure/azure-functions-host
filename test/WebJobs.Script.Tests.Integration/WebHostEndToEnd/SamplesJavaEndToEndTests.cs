// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, nameof(SamplesEndToEndTests))]
    public class SamplesJavaEndToEndTests : IClassFixture<SamplesJavaEndToEndTests.TestFixture>
    {
        private readonly ScriptSettingsManager _settingsManager;
        private TestFixture _fixture;

        public SamplesJavaEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
        }

        public object AuthorizationLevelAttribute { get; private set; }

        [Fact]
        public async Task HttpTrigger_Java_Get_Succeeds()
        {
            await InvokeHttpTrigger("HttpTrigger-Java");
        }

        private async Task<HttpResponseMessage> InvokeHttpTrigger(string functionName)
        {
            string functionKey = await _fixture.Host.GetFunctionSecretAsync($"{functionName}");
            string uri = $"api/{functionName}?code={functionKey}&name=Mathew";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            return await _fixture.Host.HttpClient.SendAsync(request);
        }

        public class TestFixture : EndToEndTestFixture
        {
            static TestFixture()
            {
                Environment.SetEnvironmentVariable("AzureWebJobs.HttpTrigger-Disabled.Disabled", "1");
            }

            public TestFixture()
                : base(Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\sample"), "samples", LanguageWorkerConstants.JavaLanguageWorkerName)
            {
            }

            public override void ConfigureJobHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureJobHost(webJobsBuilder);

                webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = new[]
                    {
                        "HttpTrigger-Java"
                    };
                });
            }
        }
    }
}