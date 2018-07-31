// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionDescriptorProviderTests : IDisposable
    {
        private readonly FunctionDescriptorProvider _provider;
        private readonly ScriptHost _host;
        private readonly ScriptSettingsManager _settingsManager;

        public FunctionDescriptorProviderTests()
        {
            string rootPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\Node");

            var host = new HostBuilder()
                .ConfigureDefaultTestScriptHost(o =>
                {
                    o.ScriptPath = rootPath;
                    o.LogPath = TestHelpers.GetHostLogFileDirectory().Parent.FullName;
                })
                .AddAzureStorage()
                .Build();
            _host = host.GetScriptHost();
            _host.InitializeAsync().GetAwaiter().GetResult();
            _provider = new TestDescriptorProvider(_host, host.Services.GetService<IOptions<ScriptHostOptions>>().Value, host.Services.GetService<ICollection<IScriptBindingProvider>>());
        }

        [Fact]
        public void ValidateFunction_DuplicateBindingNames_Throws()
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            functionMetadata.Bindings.Add(new BindingMetadata
            {
                Name = "test",
                Type = "BlobTrigger"
            });
            functionMetadata.Bindings.Add(new BindingMetadata
            {
                Name = "dupe"
            });
            functionMetadata.Bindings.Add(new BindingMetadata
            {
                Name = "dupe"
            });

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                _provider.ValidateFunction(functionMetadata);
            });

            Assert.Equal("Multiple bindings with name 'dupe' discovered. Binding names must be unique.", ex.Message);
        }

        [Fact]
        public void ValidateFunction_NoTriggerBinding_Throws()
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            functionMetadata.Bindings.Add(new BindingMetadata
            {
                Name = "test",
                Type = "Blob"
            });

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                _provider.ValidateFunction(functionMetadata);
            });

            Assert.Equal("No trigger binding specified. A function must have a trigger input binding.", ex.Message);
        }

        [Fact(Skip = "Test depending on blob/storage extension. We either need to reference them from tests or change to core bindings")]
        public void VerifyResolvedBindings_WithNoBindingMatch_ThrowsExpectedException()
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            BindingMetadata triggerMetadata = BindingMetadata.Create(JObject.Parse("{\"type\": \"blobTrigger\",\"name\": \"req\",\"direction\": \"in\", \"blobPath\": \"test\"}"));
            BindingMetadata bindingMetadata = BindingMetadata.Create(JObject.Parse("{\"type\": \"unknownbinding\",\"name\": \"blob\",\"direction\": \"in\"}"));

            functionMetadata.Bindings.Add(triggerMetadata);
            functionMetadata.Bindings.Add(bindingMetadata);

            var ex = Assert.Throws<ScriptConfigurationException>(() =>
            {
                _provider.TryCreate(functionMetadata, out FunctionDescriptor descriptor);
            });

            Assert.Contains("unknownbinding", ex.Message);
        }

        [Fact]
        public void VerifyResolvedBindings_WithValidBindingMatch_DoesNotThrow()
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            BindingMetadata triggerMetadata = BindingMetadata.Create(JObject.Parse("{\"type\": \"httpTrigger\",\"name\": \"req\",\"direction\": \"in\"}"));
            BindingMetadata bindingMetadata = BindingMetadata.Create(JObject.Parse("{\"type\": \"http\",\"name\": \"$return\",\"direction\": \"out\"}"));

            functionMetadata.Bindings.Add(triggerMetadata);
            functionMetadata.Bindings.Add(bindingMetadata);
            try
            {
                _provider.TryCreate(functionMetadata, out FunctionDescriptor descriptor);
                Assert.True(true, "No exception thrown");
            }
            catch (Exception ex)
            {
                Assert.True(false, "Exception not expected:" + ex.Message);
                throw;
            }
        }

        [Fact]
        public void CreateTriggerParameter_WithNoBindingMatch_ThrowsExpectedException()
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            BindingMetadata metadata = BindingMetadata.Create(JObject.Parse("{\"type\": \"someInvalidTrigger\",\"name\": \"req\",\"direction\": \"in\"}"));

            functionMetadata.Bindings.Add(metadata);

            var ex = Assert.Throws<ScriptConfigurationException>(() =>
            {
                _provider.TryCreate(functionMetadata, out FunctionDescriptor descriptor);
            });

            Assert.Contains("someInvalidTrigger", ex.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("_binding")]
        [InlineData("binding-test")]
        [InlineData("binding name")]
        public void ValidateBinding_InvalidName_Throws(string bindingName)
        {
            BindingMetadata bindingMetadata = new BindingMetadata
            {
                Name = bindingName
            };

            var ex = Assert.Throws<ArgumentException>(() =>
            {
                _provider.ValidateBinding(bindingMetadata);
            });

            Assert.Equal($"The binding name {bindingName} is invalid. Please assign a valid name to the binding.", ex.Message);
        }

        [Theory]
        [InlineData("bindingName")]
        [InlineData("binding1")]
        [InlineData(ScriptConstants.SystemReturnParameterBindingName)]
        public void ValidateBinding_ValidName_DoesNotThrow(string bindingName)
        {
            BindingMetadata bindingMetadata = new BindingMetadata
            {
                Name = bindingName
            };

            if (bindingMetadata.IsReturn)
            {
                bindingMetadata.Direction = BindingDirection.Out;
            }

            try
            {
                _provider.ValidateBinding(bindingMetadata);
            }
            catch (ArgumentException)
            {
                Assert.True(false, $"Valid binding name '{bindingName}' failed validation.");
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

        private class TestDescriptorProvider : FunctionDescriptorProvider
        {
            public TestDescriptorProvider(ScriptHost host, ScriptHostOptions config, ICollection<IScriptBindingProvider> bindingProviders)
                : base(host, config, bindingProviders)
            {
            }

            protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
            {
                return new Mock<IFunctionInvoker>().Object;
            }
        }
    }
}
