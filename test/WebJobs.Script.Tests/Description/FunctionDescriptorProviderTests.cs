// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
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
            ScriptHostConfiguration config = new ScriptHostConfiguration
            {
                RootScriptPath = rootPath
            };

            var environment = new Mock<IScriptHostEnvironment>();
            var eventManager = new Mock<IScriptEventManager>();
            _settingsManager = ScriptSettingsManager.Instance;
            _host = new ScriptHost(environment.Object, eventManager.Object, config, _settingsManager);
            _host.Initialize();
            _provider = new TestDescriptorProvider(_host, config);
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
            public TestDescriptorProvider(ScriptHost host, ScriptHostConfiguration config) : base(host, config)
            {
            }

            protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
            {
                return new Mock<IFunctionInvoker>().Object;
            }
        }
    }
}
