// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.WindowsAzure.Storage.Blob;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionDescriptorProviderTests
    {
        private readonly FunctionDescriptorProvider _provider;

        public FunctionDescriptorProviderTests()
        {
            string rootPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\Node");
            ScriptHostConfiguration config = new ScriptHostConfiguration
            {
                RootScriptPath = rootPath
            };
            ScriptHost host = ScriptHost.Create(config);
            _provider = new TestDescriptorProvider(host, config);
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
        public void ValidateBinding_EmptyName_Throws(string bindingName)
        {
            BindingMetadata bindingMetadata = new BindingMetadata
            {
                Name = bindingName
            };

            var ex = Assert.Throws<ArgumentException>(() =>
            {
                _provider.ValidateBinding(bindingMetadata);
            });

            Assert.Equal("A valid name must be assigned to the binding.", ex.Message);
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
