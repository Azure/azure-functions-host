// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Hosting;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class CodelessFunctionDescriptorProviderTests : IDisposable
    {
        private readonly TempDirectory _directory = new();
        private IHost _host;

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

            File.WriteAllText(Path.Combine(_directory.Path, "host.json"), "{\"version\":\"2.0\"}");
            _host = new HostBuilder().ConfigureDefaultTestWebScriptHost(b =>
            {
                b.AddAzureStorageCoreServices();
                b.AddAzureStorageBlobs();
                b.AddAzureStorageQueues();
            }, configure: options =>
            {
                options.ScriptPath = _directory.Path;
                options.LogPath = _directory.Path;
            }).Build();

            FunctionBinding functionBinding = TestHelpers.CreateBindingFromHost(_host, json);
            FunctionBinding[] bindings = new FunctionBinding[] { functionBinding };

            ParameterDescriptor descriptor = null;
            var result = CodelessFunctionDescriptorProvider.TryCreateReturnValueParameterDescriptor(typeof(string), bindings, out descriptor);
            Assert.True(result);
            Assert.Equal(ScriptConstants.SystemReturnParameterName, descriptor.Name);
            Assert.True((descriptor.Attributes & ParameterAttributes.Out) != 0);
            Assert.Equal(typeof(string).MakeByRefType(), descriptor.Type);
            Assert.Equal(1, descriptor.CustomAttributes.Count);

            result = CodelessFunctionDescriptorProvider.TryCreateReturnValueParameterDescriptor(typeof(Task<string>), bindings, out descriptor);
            Assert.True(result);
            Assert.Equal(ScriptConstants.SystemReturnParameterName, descriptor.Name);
            Assert.True((descriptor.Attributes & ParameterAttributes.Out) != 0);
            Assert.Equal(typeof(string).MakeByRefType(), descriptor.Type);
            Assert.Equal(1, descriptor.CustomAttributes.Count);

            // return type task means no return value
            result = CodelessFunctionDescriptorProvider.TryCreateReturnValueParameterDescriptor(typeof(Task), bindings, out descriptor);
            Assert.False(result);
        }

        [Fact]
        public void TryCreateReturnValueParameterDescriptor_NoReturnBinding_ReturnsExpectedValue()
        {
            JObject json = new JObject
            {
                { "type", "httpTrigger" },
                { "name", "myInput" },
                { "direction", "in" }
            };
            FunctionBinding functionBinding = TestHelpers.CreateTestBinding(json);
            FunctionBinding[] bindings = new FunctionBinding[] { functionBinding };

            ParameterDescriptor descriptor = null;
            var result = CodelessFunctionDescriptorProvider.TryCreateReturnValueParameterDescriptor(typeof(void), bindings, out descriptor);
            Assert.False(result);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _host?.Dispose();
                _directory.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
