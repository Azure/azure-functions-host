// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
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
    }
}
