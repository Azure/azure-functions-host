// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ScriptApplicationHostOptionsSetupTests
    {
        [Fact]
        public void Configure_InStandbyMode_ReturnsExpectedConfiguration()
        {
            ScriptApplicationHostOptionsSetup setup = CreateSetupWithConfiguration(true);

            var options = new ScriptApplicationHostOptions();
            setup.Configure(options);

            Assert.EndsWith(@"functions\standby\logs", options.LogPath);
            Assert.EndsWith(@"functions\standby\wwwroot", options.ScriptPath);
            Assert.EndsWith(@"functions\standby\secrets", options.SecretsPath);
            Assert.False(options.IsSelfHost);
        }

        private ScriptApplicationHostOptionsSetup CreateSetupWithConfiguration(bool inStandbyMode)
        {
            var builder = new ConfigurationBuilder();
            var configuration = builder.Build();

            var standbyOptions = new TestOptionsMonitor<StandbyOptions>(new StandbyOptions { InStandbyMode = inStandbyMode });
            var mockCache = new Mock<IOptionsMonitorCache<ScriptApplicationHostOptions>>();
            var mockServiceProvider = new Mock<IServiceProvider>();
            return new ScriptApplicationHostOptionsSetup(configuration, standbyOptions, mockCache.Object, mockServiceProvider.Object);
        }
    }
}
