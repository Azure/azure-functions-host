// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.AppService.Proxy.Client.Contract;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ProxyFunctionDescriptorProviderTests : IDisposable
    {
        private readonly ScriptHost _host;
        private readonly ScriptSettingsManager _settingsManager;

        private readonly object _proxyClient;
        private readonly ScriptHostConfiguration _config;
        private Collection<FunctionMetadata> _metadataCollection;

        public ProxyFunctionDescriptorProviderTests()
        {
            string rootPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\Proxy");
            _config = new ScriptHostConfiguration
            {
                RootScriptPath = rootPath
            };

            var environment = new Mock<IScriptHostEnvironment>();
            var eventManager = new Mock<IScriptEventManager>();
            _settingsManager = ScriptSettingsManager.Instance;
            _host = ScriptHost.Create(environment.Object, eventManager.Object, _config, _settingsManager);
            _metadataCollection = ScriptHost.ReadProxyMetadata(_config, out _proxyClient, _settingsManager);
        }

        [Fact]
        public void ReadProxyMetadata_Succeeds()
        {
            Assert.True(_metadataCollection.Count > 0);

            var metadata = _metadataCollection[0] as ProxyMetadata;

            Assert.NotNull(metadata);
            Assert.Equal(metadata.ScriptType, ScriptType.Proxy);
            Assert.Equal(metadata.UrlTemplate, "/myproxy");
        }

        [Fact]
        public void ValidateProxyFunctionDescriptor()
        {
            var proxy = _proxyClient as IProxyClient;
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

            var req = new HttpRequestMessage(HttpMethod.Get, "http://localhost/myproxy");

            var executionContext = new ExecutionContext
            {
                InvocationId = Guid.NewGuid()
            };
            var parameters = new object[] { req, executionContext };

            await proxyInvoker.Invoke(parameters);

            Assert.NotNull(req.Properties[ScriptConstants.AzureFunctionsHttpResponseKey]);

            var response = req.Properties[ScriptConstants.AzureFunctionsHttpResponseKey] as HttpResponseMessage;

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
