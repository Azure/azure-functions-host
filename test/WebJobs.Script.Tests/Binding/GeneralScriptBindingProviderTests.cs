// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class GeneralScriptBindingProviderTests
    {
        [Theory]
        [InlineData(null, null, typeof(object))]
        [InlineData(null, "many", typeof(object[]))]
        [InlineData("string", null, typeof(string))]
        [InlineData("StRing", null, typeof(string))] // case insensitive
        [InlineData("string", "mANy", typeof(string[]))] // case insensitive
        [InlineData("binary", null, typeof(byte[]))]
        [InlineData("binary", "many", typeof(byte[][]))]
        [InlineData("stream", null, typeof(Stream))]
        [InlineData("stream", "many", typeof(Stream[]))] // nonsense?
        [InlineData("string", null, typeof(ParameterBindingData), true)]
        [InlineData("string", "many", typeof(ParameterBindingData[]), true)]
        [InlineData(null, null, typeof(ParameterBindingData), true)]
        [InlineData(null, "many", typeof(ParameterBindingData[]), true)]
        public void Validate(string dataType, string cardinality, Type expectedType, bool supportsDeferredBinding = false)
        {
            var ctx = New(dataType, cardinality, supportsDeferredBinding);
            var type = GeneralScriptBindingProvider.GetRequestedType(ctx);
            Assert.Equal(expectedType, type);
        }

        private static ScriptBindingContext New(string dataType, string cardinality, bool supportsDeferredBinding)
        {
            var bindingMetadataJObject = new JObject();
            bindingMetadataJObject["type"] = "test";
            bindingMetadataJObject["direction"] = "in";
            bindingMetadataJObject["datatype"] = dataType;
            bindingMetadataJObject["cardinality"] = cardinality;
            bindingMetadataJObject["properties"] = new JObject { { "supportsDeferredBinding", supportsDeferredBinding } };

            return new ScriptBindingContext(bindingMetadataJObject);
        }

        [Fact]
        public void ManualTest()
        {
            var metadataProvider = TestHelpers.GetDefaultHost()
                .Services.GetService<IJobHostMetadataProvider>();

            var provider = new GeneralScriptBindingProvider(NullLogger<GeneralScriptBindingProvider>.Instance, metadataProvider);

            JObject bindingMetadata = new JObject
            {
                { "type", "manualTrigger" },
                { "name", "test" },
                { "direction", "in" }
            };

            ScriptBindingContext context = new ScriptBindingContext(bindingMetadata);
            ScriptBinding binding = null;
            bool created = provider.TryCreate(context, out binding);

            Assert.True(created);
            Assert.Equal(typeof(string), binding.DefaultType);

            var attr = binding.GetAttributes()[0];
            Assert.IsType<ManualTriggerAttribute>(attr);
        }

        [Fact]
        public void TryCreate_NoMatchingMetadata_DoesNotCreateBinding()
        {
            JObject bindingMetadata = new JObject
            {
                { "type", "unknown" },
                { "name", "test" },
                { "direction", "out" }
            };
            ScriptBindingContext context = new ScriptBindingContext(bindingMetadata);
            ScriptBinding binding = null;

            var mockMetadataProvider = new Mock<IJobHostMetadataProvider>();
            var provider = new GeneralScriptBindingProvider(NullLogger<GeneralScriptBindingProvider>.Instance, mockMetadataProvider.Object);
            bool created = provider.TryCreate(context, out binding);

            Assert.False(created);
            Assert.Null(binding);
        }
    }
}
