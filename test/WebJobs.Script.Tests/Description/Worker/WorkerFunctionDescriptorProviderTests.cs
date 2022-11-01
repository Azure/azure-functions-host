// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WorkerFunctionDescriptorProviderTests
    {
        private IHost _host;
        private TestWorkerDescriptorProvider _provider;

        public WorkerFunctionDescriptorProviderTests()
        {
            var scriptHostOptions = new ScriptJobHostOptions();
            var bindingProviders = new Mock<ICollection<IScriptBindingProvider>>();
            var mockApplicationLifetime = new Mock<Microsoft.AspNetCore.Hosting.IApplicationLifetime>();
            var mockFunctionInvocationDispatcher = new Mock<IFunctionInvocationDispatcher>();

            _host = new HostBuilder().ConfigureDefaultTestWebScriptHost(b =>
            {
                b.AddAzureStorage();
            }).Build();

            var scriptHost = _host.GetScriptHost();

            _provider = new TestWorkerDescriptorProvider(scriptHost, null, bindingProviders.Object, mockFunctionInvocationDispatcher.Object,
                                NullLoggerFactory.Instance, mockApplicationLifetime.Object, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void BindingAttributeContainsExpression_EmptyCollection_ReturnsFalse()
        {
            IEnumerable<FunctionBinding> bindings = new List<FunctionBinding>();
            bool result = _provider.BindingAttributeContainsExpression(bindings);
            Assert.False(result);
        }

        [Fact]
        public void BindingAttributeContainsExpression_TriggerBinding_ReturnsFalse()
        {
            var triggerBindingJObject = JObject.Parse("{\"name\":\"book\",\"direction\":\"In\",\"type\":\"blobTrigger\",\"blobPath\":\"{expression}-trigger\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{\"supportsDeferredBinding\":true}}");
            FunctionBinding triggerBinding = TestHelpers.CreateBindingFromHost(_host, triggerBindingJObject);
            IEnumerable<FunctionBinding> bindings = new List<FunctionBinding>() { triggerBinding };

            bool result = _provider.BindingAttributeContainsExpression(bindings);
            Assert.False(result);
        }

        [Fact]
        public void BindingAttributeContainsExpression__InputBinding_FindsRegexMatch_ReturnsTrue()
        {
            var inputBindingJObject = JObject.Parse("{\"name\":\"myBlob\",\"direction\":\"In\",\"type\":\"blob\",\"blobPath\":\"input-container//{id}.txt\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{\"supportsDeferredBinding\":true}}");
            FunctionBinding inputBinding = TestHelpers.CreateBindingFromHost(_host, inputBindingJObject);
            IEnumerable<FunctionBinding> bindings = new List<FunctionBinding>() { inputBinding };

            bool result = _provider.BindingAttributeContainsExpression(bindings);
            Assert.True(result);
        }

        [Fact]
        public void BindingAttributeContainsExpression_OutputBinding_FindsRegexMatch_ReturnsTrue()
        {
            var outputBindingJObject = JObject.Parse("{\"name\":\"$return\",\"direction\":\"Out\",\"type\":\"blob\",\"blobPath\":\"output-container//{name}-output.txt\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{}}");
            FunctionBinding outputBinding = TestHelpers.CreateBindingFromHost(_host, outputBindingJObject);
            IEnumerable<FunctionBinding> bindings = new List<FunctionBinding>() { outputBinding };

            bool result = _provider.BindingAttributeContainsExpression(bindings);
            Assert.True(result);
        }

        [Fact]
        public void BindingAttributeContainsExpression_DoesNotFindRegexMatch_ReturnsFalse()
        {
            var triggerBindingJObject = JObject.Parse("{\"name\":\"book\",\"direction\":\"In\",\"type\":\"blobTrigger\",\"blobPath\":\"expression-trigger\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{\"supportsDeferredBinding\":true}}");
            var inputBindingJObject = JObject.Parse("{\"name\":\"myBlob\",\"direction\":\"In\",\"type\":\"blob\",\"blobPath\":\"input-container//hello.txt\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{\"supportsDeferredBinding\":true}}");
            var outputBindingJObject = JObject.Parse("{\"name\":\"$return\",\"direction\":\"Out\",\"type\":\"blob\",\"blobPath\":\"output-container//output.txt\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{}}");

            FunctionBinding triggerBinding = TestHelpers.CreateBindingFromHost(_host, triggerBindingJObject);
            FunctionBinding inputBinding = TestHelpers.CreateBindingFromHost(_host, inputBindingJObject);
            FunctionBinding outputBinding = TestHelpers.CreateBindingFromHost(_host, outputBindingJObject);
            IEnumerable<FunctionBinding> bindings = new List<FunctionBinding>() { triggerBinding, inputBinding, outputBinding };

            bool result = _provider.BindingAttributeContainsExpression(bindings);
            Assert.False(result);
        }

        private class TestWorkerDescriptorProvider : WorkerFunctionDescriptorProvider
        {
            public TestWorkerDescriptorProvider(ScriptHost host, ScriptJobHostOptions config, ICollection<IScriptBindingProvider> bindingProviders,
                            IFunctionInvocationDispatcher dispatcher, ILoggerFactory loggerFactory, Microsoft.AspNetCore.Hosting.IApplicationLifetime applicationLifetime, TimeSpan workerInitializationTimeout)
                : base(host, config, bindingProviders, dispatcher, loggerFactory, applicationLifetime, workerInitializationTimeout)
            {
            }
        }
    }
}