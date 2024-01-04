// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class BindingMetadataExtensionsTests
    {
        [Theory]
        [InlineData("{\"name\":\"book\",\"direction\":\"In\",\"type\":\"blobTrigger\",\"blobPath\":\"expression-trigger\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{\"SupportsDeferredBinding\":\"false\"}}", false)]
        [InlineData("{\"name\":\"book\",\"direction\":\"In\",\"type\":\"blobTrigger\",\"blobPath\":\"expression-trigger\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{\"SupportsDeferredBinding\":\"true\"}}", true)]
        [InlineData("{\"name\":\"book\",\"direction\":\"In\",\"type\":\"blobTrigger\",\"blobPath\":\"expression-trigger\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{\"supportsDeferredBinding\":false}}", false)]
        [InlineData("{\"name\":\"book\",\"direction\":\"In\",\"type\":\"blobTrigger\",\"blobPath\":\"expression-trigger\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{\"supportsDeferredBinding\":true}}", true)]
        public void SupportsDeferredBinding_ReturnsExpectedBoolValue(string rawJson, bool expectedResult)
        {
            JObject bindingJObject = JObject.Parse(rawJson);
            var bindingMetadata = BindingMetadata.Create(bindingJObject);
            bool result = bindingMetadata.SupportsDeferredBinding();
            Assert.Equal(expectedResult, result);
        }

        [Fact]
        public void SupportsDeferredBinding_InvalidValue_ReturnsFalse()
        {
            JObject bindingJObject = JObject.Parse("{\"name\":\"book\",\"direction\":\"In\",\"type\":\"blobTrigger\",\"blobPath\":\"expression-trigger\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{\"skipDeferredBinding\":\"blah\"}}");
            var bindingMetadata = BindingMetadata.Create(bindingJObject);
            bool result = bindingMetadata.SupportsDeferredBinding();
            Assert.False(result);
        }

        [Theory]
        [InlineData("{\"name\":\"book\",\"direction\":\"In\",\"type\":\"blobTrigger\",\"blobPath\":\"expression-trigger\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{\"SkipDeferredBinding\":\"false\"}}", false)]
        [InlineData("{\"name\":\"book\",\"direction\":\"In\",\"type\":\"blobTrigger\",\"blobPath\":\"expression-trigger\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{\"SkipDeferredBinding\":\"true\"}}", true)]
        [InlineData("{\"name\":\"book\",\"direction\":\"In\",\"type\":\"blobTrigger\",\"blobPath\":\"expression-trigger\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{\"skipDeferredBinding\":false}}", false)]
        [InlineData("{\"name\":\"book\",\"direction\":\"In\",\"type\":\"blobTrigger\",\"blobPath\":\"expression-trigger\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{\"skipDeferredBinding\":true}}", true)]
        public void SkipDeferredBinding_ReturnsExpectedBoolValue(string rawJson, bool expectedResult)
        {
            JObject bindingJObject = JObject.Parse(rawJson);
            var bindingMetadata = BindingMetadata.Create(bindingJObject);
            bool result = bindingMetadata.SkipDeferredBinding();
            Assert.Equal(expectedResult, result);
        }

        [Fact]
        public void SkipDeferredBinding_InvalidValue_ReturnsFalse()
        {
            JObject bindingJObject = JObject.Parse("{\"name\":\"book\",\"direction\":\"In\",\"type\":\"blobTrigger\",\"blobPath\":\"expression-trigger\",\"connection\":\"AzureWebJobsStorage\",\"properties\":{\"skipDeferredBinding\":\"blah\"}}");
            var bindingMetadata = BindingMetadata.Create(bindingJObject);
            bool result = bindingMetadata.SkipDeferredBinding();
            Assert.False(result);
        }

        [Theory]
        [InlineData("httpTrigger", true)]
        [InlineData("otherTrigger", false)]
        [InlineData("http2Trigger", false)]
        [InlineData("inputBinding", false)]
        public void IsHttpTrigger_ReturnsExpectedValue(string type, bool expected)
        {
            var bindingMetadata = new BindingMetadata
            {
                Type = type,
            };

            Assert.Equal(expected, bindingMetadata.IsHttpTrigger());
        }

        [Theory]
        [InlineData("eventGridTrigger", true)]
        [InlineData("blobTrigger", true, "eventGrid")]
        [InlineData("blobTrigger", false, "other")]
        [InlineData("httpTrigger", false)]
        [InlineData("inputBinding", false)]
        public void IsWebHookTrigger_ReturnsExpectedValue(string type, bool expected, string source = null)
        {
            var bindingMetadata = new BindingMetadata
            {
                Type = type,
            };

            if (source is not null)
            {
                bindingMetadata.Raw = new JObject
                {
                    ["source"] = source,
                };
            }

            Assert.Equal(expected, bindingMetadata.IsHttpTrigger());
        }

        [Theory]
        [InlineData("orchestrationTrigger", true)]
        [InlineData("activityTrigger", true)]
        [InlineData("entityTrigger", true)]
        [InlineData("httpTrigger", false)]
        [InlineData("inputBinding", false)]
        public void IsDurableTrigger_ReturnsExpectedValue(string type, bool expected)
        {
            var bindingMetadata = new BindingMetadata
            {
                Type = type,
            };

            Assert.Equal(expected, bindingMetadata.IsDurableTrigger());
        }
    }
}