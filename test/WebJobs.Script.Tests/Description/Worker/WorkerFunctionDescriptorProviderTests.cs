// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WorkerFunctionDescriptorProviderTests : IDisposable
    {
        private IHost _host;
        private TestWorkerDescriptorProvider _provider;

        public WorkerFunctionDescriptorProviderTests()
        {
            var mockApplicationLifetime = new Mock<Microsoft.AspNetCore.Hosting.IApplicationLifetime>();
            var mockFunctionInvocationDispatcher = new Mock<IFunctionInvocationDispatcher>();

            string rootPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\Node");

            _host = new HostBuilder().ConfigureDefaultTestWebScriptHost(b =>
            {
                b.AddAzureStorage();
            },
            o =>
            {
                o.ScriptPath = rootPath;
                o.LogPath = TestHelpers.GetHostLogFileDirectory().Parent.FullName;
            })
            .Build();

            var scriptHost = _host.GetScriptHost();
            scriptHost.InitializeAsync().GetAwaiter().GetResult();

            var config = _host.Services.GetService<IOptions<ScriptJobHostOptions>>().Value;
            var providers = _host.Services.GetService<ICollection<IScriptBindingProvider>>();

            _provider = new TestWorkerDescriptorProvider(scriptHost, config, providers, mockFunctionInvocationDispatcher.Object,
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
        public void BindingAttributeContainsExpression__InputBinding_EmptyConnection_FindsRegexMatch_ReturnsTrue()
        {
            var inputBindingJObject = JObject.Parse("{\"name\":\"myBlob\",\"direction\":\"In\",\"type\":\"blob\",\"blobPath\":\"input-container//{id}.txt\",\"connection\":\"\",\"properties\":{\"supportsDeferredBinding\":true}}");
            FunctionBinding inputBinding = TestHelpers.CreateBindingFromHost(_host, inputBindingJObject);
            IEnumerable<FunctionBinding> bindings = new List<FunctionBinding>() { inputBinding };

            bool result = _provider.BindingAttributeContainsExpression(bindings);
            Assert.True(result);
        }

        [Fact]
        public void BindingAttributeContainsExpression__InputBinding_EmptyConnection_NoRegex_FindsRegexMatch_ReturnsFalse()
        {
            var inputBindingJObject = JObject.Parse("{\"name\":\"myBlob\",\"direction\":\"In\",\"type\":\"blob\",\"blobPath\":\"input-container//file.txt\",\"connection\":\"\",\"properties\":{\"supportsDeferredBinding\":true}}");
            FunctionBinding inputBinding = TestHelpers.CreateBindingFromHost(_host, inputBindingJObject);
            IEnumerable<FunctionBinding> bindings = new List<FunctionBinding>() { inputBinding };

            bool result = _provider.BindingAttributeContainsExpression(bindings);
            Assert.False(result);
        }

        [Fact]
        public void BindingAttributeContainsExpression__InputBinding_EmptyFields_FindsRegexMatch_ReturnsFalse()
        {
            var inputBindingJObject = JObject.Parse("{\"name\":\"myBlob\",\"direction\":\"In\",\"type\":\"blob\",\"blobPath\":\"\",\"connection\":\"\",\"properties\":{\"supportsDeferredBinding\":true}}");
            FunctionBinding inputBinding = TestHelpers.CreateBindingFromHost(_host, inputBindingJObject);
            IEnumerable<FunctionBinding> bindings = new List<FunctionBinding>() { inputBinding };

            bool result = _provider.BindingAttributeContainsExpression(bindings);
            Assert.False(result);
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
        public void BindingAttributeContainsExpression_OutputBinding_EmptyConnection_FindsRegexMatch_ReturnsTrue()
        {
            var outputBindingJObject = JObject.Parse("{\"name\":\"$return\",\"direction\":\"Out\",\"type\":\"blob\",\"blobPath\":\"output-container//{name}-output.txt\",\"connection\":\"\",\"properties\":{}}");
            FunctionBinding outputBinding = TestHelpers.CreateBindingFromHost(_host, outputBindingJObject);
            IEnumerable<FunctionBinding> bindings = new List<FunctionBinding>() { outputBinding };

            bool result = _provider.BindingAttributeContainsExpression(bindings);
            Assert.True(result);
        }

        [Fact]
        public void BindingAttributeContainsExpression_OutputBinding_EmptyFields_FindsRegexMatch_ReturnsFalse()
        {
            var outputBindingJObject = JObject.Parse("{\"name\":\"$return\",\"direction\":\"Out\",\"type\":\"blob\",\"blobPath\":\"\",\"connection\":\"\",\"properties\":{}}");
            FunctionBinding outputBinding = TestHelpers.CreateBindingFromHost(_host, outputBindingJObject);
            IEnumerable<FunctionBinding> bindings = new List<FunctionBinding>() { outputBinding };

            bool result = _provider.BindingAttributeContainsExpression(bindings);
            Assert.False(result);
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

        [Theory]
        [InlineData(true, true, typeof(byte[]))]
        [InlineData(false, true, typeof(byte[]))]
        [InlineData(false, false, typeof(byte[]))]
        public async Task CreateTriggerParameter_DeferredBindingFlags_SetsTriggerType(bool supportsDeferredBinding, bool skipDeferredBinding, Type expectedType)
        {
            string bindingJson = $@"{{""name"":""book"",""direction"":""In"",""type"":""blobTrigger"",""blobPath"":""expression-trigger"",""connection"":""AzureWebJobsStorage"",""properties"":{{""SupportsDeferredBinding"":""{supportsDeferredBinding}""}}}}";

            BindingMetadata metadata = BindingMetadata.Create(JObject.Parse(bindingJson));
            metadata.Properties.Add("SkipDeferredBinding", skipDeferredBinding);

            FunctionMetadata functionMetadata = new FunctionMetadata();
            functionMetadata.Bindings.Add(metadata);

            try
            {
                var (created, descriptor) = await _provider.TryCreate(functionMetadata);
                Assert.Equal(expectedType, descriptor.TriggerParameter.Type);
            }
            catch (Exception ex)
            {
                Assert.True(false, "Exception not expected:" + ex.Message);
                throw;
            }
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