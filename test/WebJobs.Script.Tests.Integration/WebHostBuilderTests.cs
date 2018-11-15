// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization;
using Microsoft.Azure.WebJobs.Script.WebHost.Standby;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration
{
    public class WebHostBuilderTests
    {
        private IWebHost _webHost;

        public WebHostBuilderTests()
        {
            var builder = Program.CreateWebHostBuilder();
            _webHost = builder.Build();
        }

        [Theory]
        [InlineData(typeof(WebJobsScriptHostService), typeof(WebJobsScriptHostService))]
        [InlineData(typeof(IScriptHostManager), typeof(WebJobsScriptHostService))]
        [InlineData(typeof(IScriptWebHostEnvironment), typeof(ScriptWebHostEnvironment))]
        [InlineData(typeof(IStandbyManager), typeof(StandbyManager))]
        [InlineData(typeof(IScriptHostBuilder), typeof(DefaultScriptHostBuilder))]
        [InlineData(typeof(ScriptSettingsManager), typeof(ScriptSettingsManager))]
        [InlineData(typeof(IEventGenerator), typeof(EtwEventGenerator))]
        [InlineData(typeof(IWebFunctionsManager), typeof(WebFunctionsManager))]
        [InlineData(typeof(IInstanceManager), typeof(InstanceManager))]
        [InlineData(typeof(HttpClient), typeof(HttpClient))]
        [InlineData(typeof(IFileSystem), typeof(FileSystem))]
        [InlineData(typeof(ISecretManagerProvider), typeof(DefaultSecretManagerProvider))]
        [InlineData(typeof(IHostIdProvider), typeof(ScriptHostIdProvider))]
        [InlineData(typeof(IScriptEventManager), typeof(ScriptEventManager))]
        [InlineData(typeof(IDebugManager), typeof(DebugManager))]
        [InlineData(typeof(IDebugStateProvider), typeof(DebugStateProvider))]
        [InlineData(typeof(IEnvironment), typeof(SystemEnvironment))]
        [InlineData(typeof(HostPerformanceManager), typeof(HostPerformanceManager))]
        [InlineData(typeof(IOptionsChangeTokenSource<ScriptApplicationHostOptions>), typeof(StandbyChangeTokenSource))]
        [InlineData(typeof(IHostedService), new Type[] { typeof(WebJobsScriptHostService) })]
        [InlineData(typeof(VirtualFileSystem), typeof(VirtualFileSystem))]
        [InlineData(typeof(VirtualFileSystemMiddleware), typeof(VirtualFileSystemMiddleware))]
        [InlineData(typeof(IAuthorizationHandler), new Type[] { typeof(AuthLevelAuthorizationHandler), typeof(NamedAuthLevelAuthorizationHandler), typeof(FunctionAuthorizationHandler) })]
        public void Build_RegistersExectedServices(Type expectedServiceType, object expected)
        {
            var expectedImplementationTypes = expected as Type[];
            if (expectedImplementationTypes != null)
            {
                var services = _webHost.Services.GetServices(expectedServiceType);
                var serviceTypes = services.Select(p => p.GetType()).ToHashSet();
                foreach (var expectedImplementationType in expectedImplementationTypes)
                {
                    Assert.True(serviceTypes.Contains(expectedImplementationType));
                }
            }
            else
            {
                Type expectedImplementationType = (Type)expected;
                var service = _webHost.Services.GetRequiredService(expectedServiceType);
                Assert.Equal(expectedImplementationType, service.GetType());
            }
        }
    }
}