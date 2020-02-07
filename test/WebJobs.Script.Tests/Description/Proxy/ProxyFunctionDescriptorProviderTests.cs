// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.AppService.Proxy.Client;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ProxyFunctionDescriptorProviderTests : IAsyncLifetime, IDisposable
    {
        private readonly ScriptHost _scriptHost;
        private readonly ScriptSettingsManager _settingsManager;
        private readonly IHost _host;
        private readonly ProxyClientExecutor _proxyClient;
        private ImmutableArray<FunctionMetadata> _metadataCollection;

        public ProxyFunctionDescriptorProviderTests()
        {
            string rootPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\Proxies");

            var hostOptions = new JobHostOptions();
            var eventManager = new Mock<IScriptEventManager>();
            var contextFactory = new Mock<IJobHostContextFactory>();

            _proxyClient = GetMockProxyClient();
            _settingsManager = ScriptSettingsManager.Instance;
            _host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost(o =>
                {
                    o.ScriptPath = rootPath;
                    o.LogPath = TestHelpers.GetHostLogFileDirectory().Parent.FullName;
                })
                .Build();

            _scriptHost = _host.GetScriptHost();
        }

        public async Task InitializeAsync()
        {
            await _scriptHost.StartAsync();

            _metadataCollection = _host.Services.GetService<IProxyMetadataManager>()
                .ProxyMetadata.Functions;
        }

        public async Task DisposeAsync()
        {
            await _scriptHost.StopAsync();
        }

        private ProxyClientExecutor GetMockProxyClient()
        {
            var proxyClient = new Mock<IProxyClient>();

            ProxyData proxyData = new ProxyData();
            proxyData.Routes.Add(new Routes("/myproxy", "test", new[] { HttpMethod.Get, HttpMethod.Post }));

            proxyData.Routes.Add(new Routes("/mymockhttp", "localFunction", new[] { HttpMethod.Get }));

            proxyClient.Setup(p => p.GetProxyData()).Returns(proxyData);

            proxyClient.Setup(p => p.CallAsync(It.IsAny<object[]>(), It.IsAny<IFuncExecutor>(), It.IsAny<ILogger>())).Returns(
                (object[] arguments, IFuncExecutor funcExecutor, ILogger logger) =>
                {
                    object requestObj = arguments != null && arguments.Length > 0 ? arguments[0] : null;
                    var request = requestObj as HttpRequest;
                    var response = new StatusCodeResult(StatusCodes.Status200OK);
                    request.HttpContext.Response.Headers.Add("myversion", "123");
                    request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey] = response;
                    return Task.CompletedTask;
                });

            return new ProxyClientExecutor(proxyClient.Object);
        }

        [Fact]
        public async Task ValidateProxyFunctionDescriptor()
        {
            var proxy = _proxyClient as ProxyClientExecutor;
            Assert.NotNull(proxy);

            var proxyFunctionDescriptor = new ProxyFunctionDescriptorProvider(_scriptHost, _scriptHost.ScriptOptions, _host.Services.GetService<ICollection<IScriptBindingProvider>>(), proxy, NullLoggerFactory.Instance);

            var (created, functionDescriptor) = await proxyFunctionDescriptor.TryCreate(_metadataCollection[0]);

            Assert.True(created);

            var proxyInvoker = functionDescriptor.Invoker as ProxyFunctionInvoker;

            Assert.NotNull(proxyInvoker);
        }

        [Fact]
        public async Task ValidateProxyFunctionInvoker()
        {
            var proxyFunctionDescriptor = new ProxyFunctionDescriptorProvider(_scriptHost, _scriptHost.ScriptOptions, _host.Services.GetService<ICollection<IScriptBindingProvider>>(), _proxyClient, NullLoggerFactory.Instance);

            var (created, functionDescriptor) = await proxyFunctionDescriptor.TryCreate(_metadataCollection[0]);

            var proxyInvoker = functionDescriptor.Invoker as ProxyFunctionInvoker;

            var req = HttpTestHelpers.CreateHttpRequest("GET", "http://localhost/myproxy");

            var executionContext = new ExecutionContext
            {
                InvocationId = Guid.NewGuid()
            };
            var parameters = new object[] { req, executionContext };

            await proxyInvoker.Invoke(parameters);

            Assert.NotNull(req.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey]);

            var response = req.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey] as IActionResult;

            Assert.NotNull(response);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _scriptHost?.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
