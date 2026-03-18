// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Abstractions.Tests
{
    public class BindingMetadataTests
    {
        [Fact]
        public void BindingMetadata_Create_TriggerBinding_Success()
        {
            JObject triggerBinding = JObject.Parse("{\"name\":\"book\",\"direction\":\"In\",\"type\":\"blobTrigger\",\"blobPath\":\"expression-trigger\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{\"supportsDeferredBinding\":false}}");
            var result = BindingMetadata.Create(triggerBinding);

            Assert.Equal("book", result.Name);
            Assert.Equal("blobTrigger", result.Type);
            Assert.Equal("AzureWebJobsStorage", result.Connection);
            Assert.Equal(triggerBinding, result.Raw);
            Assert.Equal(BindingDirection.In, result.Direction);
            Assert.True(result.Properties.TryGetValue("SupportsDeferredBinding", out var supportsDeferredBinding));
            Assert.False((bool)supportsDeferredBinding);
            Assert.True(result.IsTrigger);
            Assert.False(result.IsReturn);
        }

        [Fact]
        public void BindingMetadata_Create_InputBinding_Success()
        {
            JObject inputBinding = JObject.Parse("{\"name\":\"myBlob\",\"direction\":\"In\",\"type\":\"blob\",\"blobPath\":\"input-container//hello.txt\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{\"supportsDeferredBinding\":true}}");
            var result = BindingMetadata.Create(inputBinding);

            Assert.Equal("myBlob", result.Name);
            Assert.Equal("blob", result.Type);
            Assert.Equal("AzureWebJobsStorage", result.Connection);
            Assert.Equal(inputBinding, result.Raw);
            Assert.Equal(BindingDirection.In, result.Direction);
            Assert.True(result.Properties.TryGetValue("SupportsDeferredBinding", out var supportsDeferredBinding));
            Assert.True((bool)supportsDeferredBinding);
            Assert.False(result.IsTrigger);
            Assert.False(result.IsReturn);
        }

        [Fact]
        public void BindingMetadata_Create_OutputBinding_Success()
        {
            JObject outputBinding = JObject.Parse("{\"name\":\"$return\",\"direction\":\"Out\",\"type\":\"blob\",\"blobPath\":\"output-container//output.txt\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{}}");
            var result = BindingMetadata.Create(outputBinding);

            Assert.Equal("$return", result.Name);
            Assert.Equal("blob", result.Type);
            Assert.Equal("AzureWebJobsStorage", result.Connection);
            Assert.Equal(outputBinding, result.Raw);
            Assert.Equal(BindingDirection.Out, result.Direction);
            Assert.Empty(result.Properties);
            Assert.False(result.IsTrigger);
            Assert.True(result.IsReturn);
        }

        [Fact]
        public void BindingMetadata_Create_NullJObject_Throws()
        {
            Action act = () => BindingMetadata.Create(null);
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(act);
            Assert.Equal("Value cannot be null. (Parameter 'raw')", exception.Message);
        }

        [Fact]
        public void BindingMetadata_Create_InvalidDirectionFormat_Throws()
        {
            JObject outputBinding = JObject.Parse("{\"name\":\"$return\",\"direction\":\"hi\",\"type\":\"blob\",\"blobPath\":\"output-container//output.txt\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{}}");
            Action act = () => BindingMetadata.Create(outputBinding);
            FormatException exception = Assert.Throws<FormatException>(act);
            Assert.Equal("'hi' is not a valid binding direction.", exception.Message);
        }

        [Fact]
        public void BindingMetadata_Create_PropertiesIsNull_CreatesEmptyDict()
        {
            JObject inputBinding = JObject.Parse("{\"name\":\"myBlob\",\"direction\":\"In\",\"type\":\"blob\",\"blobPath\":\"input-container//hello.txt\",\"connection\":\"AzureWebJobsStorage\"}");
            var result = BindingMetadata.Create(inputBinding);
            Assert.Empty(result.Properties);
        }

        [Fact]
        public void BindingMetadata_Properties_IsCaseInsensitive()
        {
            JObject binding = JObject.Parse("{\"name\":\"test\",\"direction\":\"In\",\"type\":\"blob\",\"properties\":{\"testProperty\":\"value\"}}");
            var result = BindingMetadata.Create(binding);

            Assert.True(result.Properties.TryGetValue("testProperty", out var value1));
            Assert.Equal("value", value1);

            Assert.True(result.Properties.TryGetValue("TestProperty", out var value2));
            Assert.Equal("value", value2);

            Assert.True(result.Properties.TryGetValue("TESTPROPERTY", out var value3));
            Assert.Equal("value", value3);

            Assert.True(result.Properties.TryGetValue("testproperty", out var value4));
            Assert.Equal("value", value4);
        }

        [Fact]
        public void BindingMetadata_Properties_CaseInsensitive_ContainsKey()
        {
            JObject binding = JObject.Parse("{\"name\":\"test\",\"direction\":\"In\",\"type\":\"blob\",\"properties\":{\"myKey\":\"myValue\"}}");
            var result = BindingMetadata.Create(binding);

            Assert.True(result.Properties.ContainsKey("myKey"));
            Assert.True(result.Properties.ContainsKey("MyKey"));
            Assert.True(result.Properties.ContainsKey("MYKEY"));
            Assert.True(result.Properties.ContainsKey("mykey"));
        }

        [Fact]
        public void BindingMetadata_Constructor_PropertiesIsCaseInsensitive()
        {
            var metadata = new BindingMetadata
            {
                Name = "test",
                Type = "blob",
                Direction = BindingDirection.In
            };

            metadata.Properties["TestKey"] = "value1";

            Assert.True(metadata.Properties.TryGetValue("testkey", out var value));
            Assert.Equal("value1", value);
            Assert.True(metadata.Properties.ContainsKey("TESTKEY"));
            Assert.Single(metadata.Properties);
        }

        [Fact]
        public void BindingMetadata_Properties_CaseInsensitive_MultipleProperties()
        {
            JObject binding = JObject.Parse("{\"name\":\"test\",\"direction\":\"In\",\"type\":\"blob\",\"properties\":{\"firstProperty\":\"first\",\"secondProperty\":\"second\",\"thirdProperty\":\"third\"}}");
            var result = BindingMetadata.Create(binding);

            Assert.Equal(3, result.Properties.Count);

            Assert.True(result.Properties.TryGetValue("FirstProperty", out var first));
            Assert.Equal("first", first);

            Assert.True(result.Properties.TryGetValue("SECONDPROPERTY", out var second));
            Assert.Equal("second", second);

            Assert.True(result.Properties.TryGetValue("thirdproperty", out var third));
            Assert.Equal("third", third);
        }
    }
}
