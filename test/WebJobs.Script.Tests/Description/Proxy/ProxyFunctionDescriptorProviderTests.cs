// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.AppService.Proxy.Client;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ProxyFunctionDescriptorProviderTests : IDisposable
    {
        private readonly ScriptHost _host;
        private readonly ScriptSettingsManager _settingsManager;

        private readonly ProxyClientExecutor _proxyClient;
        private readonly ScriptHostConfiguration _config;
        private Collection<FunctionMetadata> _metadataCollection;

        public ProxyFunctionDescriptorProviderTests()
        {
            string rootPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\Proxies");
            _config = new ScriptHostConfiguration
            {
                RootScriptPath = rootPath
            };

            var environment = new Mock<IScriptHostEnvironment>();
            var eventManager = new Mock<IScriptEventManager>();

            _proxyClient = GetMockProxyClient();
            _settingsManager = ScriptSettingsManager.Instance;
            _host = new ScriptHost(environment.Object, eventManager.Object, _config, _settingsManager, proxyClient: _proxyClient);
            _host.Initialize();
            _metadataCollection = _host.ReadProxyMetadata(_config, _settingsManager);
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
        public void ValidateProxyFunctionDescriptor()
        {
            var proxy = _proxyClient as ProxyClientExecutor;
            Assert.NotNull(proxy);

            var proxyFunctionDescriptor = new ProxyFunctionDescriptorProvider(_host, _config, proxy);

            Assert.True(proxyFunctionDescriptor.TryCreate(_metadataCollection[0], out FunctionDescriptor functionDescriptor));

            var proxyInvoker = functionDescriptor.Invoker as ProxyFunctionInvoker;

            Assert.NotNull(proxyInvoker);
        }

        [Fact]
        public async Task ValidateProxyFunctionInvoker()
        {
            var proxyFunctionDescriptor = new ProxyFunctionDescriptorProvider(_host, _config, _proxyClient);

            proxyFunctionDescriptor.TryCreate(_metadataCollection[0], out FunctionDescriptor functionDescriptor);

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
                _host?.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
