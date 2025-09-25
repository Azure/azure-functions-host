// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net.Http;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.ExtensionBundle
{
    public class ExtensionBundleHttpExtensionsTests
    {
        [Fact]
        public void TryGetAzureRef_ReturnsOfficialSampleValue()
        {
            var headerValue = "0zxV+XAAAAABKMMOjBv2NT4TY6SQVjC0zV1NURURHRTA2MTkANDM3YzgyY2QtMzYwYS00YTU0LTk0YzMtNWZmNzA3NjQ3Nzgz";
            var resp = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            resp.Headers.Add(ExtensionBundleHttpExtensions.AzureRefHeaderName, headerValue);
            Assert.True(resp.TryGetAzureRef(out var result));
            Assert.Equal(headerValue, result);
        }

        // Sanitization/length truncation removed: ensure full value is returned.
        [Fact]
        public void TryGetAzureRef_LongValue_Preserved()
        {
            var longVal = new string('a', 140);
            var resp = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            resp.Headers.Add(ExtensionBundleHttpExtensions.AzureRefHeaderName, longVal);
            Assert.True(resp.TryGetAzureRef(out var result));
            Assert.Equal(longVal, result);
        }

        [Fact]
        public void TryGetAzureRef_Absent_ReturnsFalse()
        {
            var resp = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            Assert.False(resp.TryGetAzureRef(out var result));
            Assert.Null(result);
        }

        [Fact]
        public void TryGetAzureRef_MultipleValues_PicksFirst()
        {
            var resp = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            resp.Headers.Add(
                ExtensionBundleHttpExtensions.AzureRefHeaderName,
                new[] { "first-value", "second-value" });

            Assert.True(resp.TryGetAzureRef(out var result));
            Assert.Equal("first-value", result);
        }

        [Fact]
        public void TryGetAzureRef_NullResponse_Throws()
        {
            HttpResponseMessage resp = null;
            Assert.Throws<System.ArgumentNullException>(() => ExtensionBundleHttpExtensions.TryGetAzureRef(resp, out _));
        }
    }
}
