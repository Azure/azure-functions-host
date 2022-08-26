﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading.Tasks;
using Az.ServerlessSecurity.Platform;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ServerlessSecurityDefenderOptionsSetupTests
    {
        [Fact]
        public void ServerlessSecurityServiceOptionsSetup_DefaulValues()
        {
            var mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureFunctionsSecurityAgentEnabled)).Returns<string>(null);

            var setup = new ServerlessSecurityDefenderOptionsSetup(mockEnvironment.Object);
            var options = new ServerlessSecurityDefenderOptions();
            setup.Configure(options);

            Assert.False(options.EnableDefender);

            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureFunctionsSecurityAgentEnabled)).Returns("0");

            setup = new ServerlessSecurityDefenderOptionsSetup(mockEnvironment.Object);
            options = new ServerlessSecurityDefenderOptions();
            setup.Configure(options);

            Assert.False(options.EnableDefender);
        }

        [Fact]
        public void ServerlessSecurityServiceOptionsSetup_EnabledValues()
        {
            var mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureFunctionsSecurityAgentEnabled)).Returns("1");

            var setup = new ServerlessSecurityDefenderOptionsSetup(mockEnvironment.Object);
            var options = new ServerlessSecurityDefenderOptions();
            setup.Configure(options);

            Assert.True(options.EnableDefender);
        }
    }
}
