// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.WebJobs.Script.Tests;

namespace Microsoft.Azure.WebJobs.Script.Tests.ApplicationInsights
{
    public abstract class ApplicationInsightsTestFixture : EndToEndTestFixture
    {
        public const string ApplicationInsightsKey = "some_key";

        public ApplicationInsightsTestFixture(string rootPath, string testId, string language) :
            base(rootPath, testId, language)
        {

        }

        protected override ExtensionPackageReference[] GetExtensionsToInstall()
        {
            return new ExtensionPackageReference[]
            {
                new ExtensionPackageReference
                {
                    Id = "Microsoft.Azure.WebJobs.Extensions.ApplicationInsights",
                    Version = "1.0.0"
                }
            };
        }

        public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
        {
            webJobsBuilder.Services.AddSingleton<ITelemetryChannel>(_ => Channel);
            webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
            {
                o.Functions = new[]
                {
                    "Scenarios",
                    "HttpTrigger-Scenarios"
                };
            });
        }

        public override void ConfigureAppConfiguration(IConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string>
            {
                [EnvironmentSettingNames.AppInsightsInstrumentationKey] = ApplicationInsightsKey
            });
        }

        public TestTelemetryChannel Channel { get; private set; } = new TestTelemetryChannel();

        public TestFunctionHost TestHost { get; }

        public ScriptApplicationHostOptions WebHostOptions { get; private set; }

        public HttpClient HttpClient { get; private set; }

        public override async Task DisposeAsync()
        {
            await base.DisposeAsync();

            // App Insights takes 2 seconds to flush telemetry and because our container
            // is disposed on a background task, it doesn't block. So waiting here to ensure
            // everything is flushed and can't affect subsequent tests.
            Thread.Sleep(2000);
        }
    }
}
