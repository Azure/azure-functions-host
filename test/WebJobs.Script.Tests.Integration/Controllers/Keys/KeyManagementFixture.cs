﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using Autofac;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers
{
    public class KeyManagementFixture : ControllerScenarioTestFixture
    {
        private readonly string _testFunctionName = "httptrigger-csharp";

        public Dictionary<string, string> TestFunctionKeys { get; set; }

        public Dictionary<string, string> TestSystemKeys { get; set; }

        public Mock<TestSecretManager> SecretManagerMock { get; set; }

        public virtual string TestKeyScope => _testFunctionName;

        public virtual ScriptSecretsType SecretsType => ScriptSecretsType.Function;

        protected override void ConfigureJobHostBuilder(IHostBuilder builder)
        {
            TestFunctionKeys = new Dictionary<string, string>
            {
                { "key1", "1234" },
                { "key2", "1234" }
            };

            SecretManagerMock = BuildSecretManager();

            builder.ConfigureServices(c => c.AddSingleton<ISecretManager>(SecretManagerMock.Object));

            base.ConfigureJobHostBuilder(builder);
        }

        public static ApiModel ReadApiModelContent(HttpResponseMessage response)
        {
            var result = response.Content.ReadAsAsync<JObject>().Result;

            var apimodel = new ApiModel();
            apimodel.Merge(result);

            if (result?["links"] != null)
            {
                apimodel.Links = result["links"].ToObject<Collection<Link>>();
            }

            return apimodel;
        }

        protected virtual Mock<TestSecretManager> BuildSecretManager()
        {
            var manager = new Mock<TestSecretManager>();
            manager.CallBase = true;
            manager.Setup(s => s.GetFunctionSecretsAsync(_testFunctionName, false))
                .ReturnsAsync(() => TestFunctionKeys);

            return manager;
        }
    }
}
