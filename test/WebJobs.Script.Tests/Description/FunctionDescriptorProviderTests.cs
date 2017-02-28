﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Moq;
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
            _settingsManager = ScriptSettingsManager.Instance;
            _host = ScriptHost.Create(environment.Object, config, _settingsManager);
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
                throw new NotImplementedException();
            }
        }
    }
}
