// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class DotNetFunctionDescriptorProviderTests
    {
        [Fact]
        public void TryCreateReturnValueParameterDescriptor_ReturnBindingPresent_ReturnsExpectedValue()
        {
            JObject json = new JObject
            {
                { "type", "blob" },
                { "name", ScriptConstants.SystemReturnParameterBindingName },
                { "direction", "out" },
                { "path", "foo/bar" }
            };
            FunctionBinding functionBinding = CreateTestBlobBinding(json);
            FunctionBinding[] bindings = new FunctionBinding[] { functionBinding };

            ParameterDescriptor descriptor = null;
            var result = DotNetFunctionDescriptorProvider.TryCreateReturnValueParameterDescriptor(typeof(string), bindings, out descriptor);
            Assert.True(result);
            Assert.Equal(ScriptConstants.SystemReturnParameterName, descriptor.Name);
            Assert.True((descriptor.Attributes & ParameterAttributes.Out) != 0);
            Assert.Equal(typeof(string).MakeByRefType(), descriptor.Type);
            Assert.Equal(1, descriptor.CustomAttributes.Count);

            result = DotNetFunctionDescriptorProvider.TryCreateReturnValueParameterDescriptor(typeof(Task<string>), bindings, out descriptor);
            Assert.True(result);
            Assert.Equal(ScriptConstants.SystemReturnParameterName, descriptor.Name);
            Assert.True((descriptor.Attributes & ParameterAttributes.Out) != 0);
            Assert.Equal(typeof(string).MakeByRefType(), descriptor.Type);
            Assert.Equal(1, descriptor.CustomAttributes.Count);

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                DotNetFunctionDescriptorProvider.TryCreateReturnValueParameterDescriptor(typeof(Task), bindings, out descriptor);
            });
            Assert.Equal($"{ScriptConstants.SystemReturnParameterBindingName} cannot be bound to return type {typeof(Task).Name}.", ex.Message);
        }

        [Fact]
        public void TryCreateReturnValueParameterDescriptor_NoReturnBinding_ReturnsExpectedValue()
        {
            JObject json = new JObject
            {
                { "type", "blob" },
                { "name", "myOutput" },
                { "direction", "out" },
                { "path", "foo/bar" }
            };
            FunctionBinding functionBinding = CreateTestBlobBinding(json);
            FunctionBinding[] bindings = new FunctionBinding[] { functionBinding };

            ParameterDescriptor descriptor = null;
            var result = DotNetFunctionDescriptorProvider.TryCreateReturnValueParameterDescriptor(typeof(string), bindings, out descriptor);
            Assert.False(result);
        }

        private static FunctionBinding CreateTestBlobBinding(JObject json)
        {
            ScriptBindingContext context = new ScriptBindingContext(json);
            WebJobsCoreScriptBindingProvider provider = new WebJobsCoreScriptBindingProvider(new JobHostConfiguration(), new JObject(), new TestTraceWriter(TraceLevel.Verbose));
            ScriptBinding scriptBinding = null;
            provider.TryCreate(context, out scriptBinding);
            BindingMetadata bindingMetadata = BindingMetadata.Create(json);
            ScriptHostConfiguration config = new ScriptHostConfiguration();
            return new ExtensionBinding(config, scriptBinding, bindingMetadata);
        }
    }
}
