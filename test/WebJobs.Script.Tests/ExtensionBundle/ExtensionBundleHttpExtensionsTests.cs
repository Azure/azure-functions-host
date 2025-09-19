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
        public void GetAzureRef_ReturnsOfficialSampleValue()
        {
            var headerValue = "0zxV+XAAAAABKMMOjBv2NT4TY6SQVjC0zV1NURURHRTA2MTkANDM3YzgyY2QtMzYwYS00YTU0LTk0YzMtNWZmNzA3NjQ3Nzgz";
            var resp = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            resp.Headers.Add(ExtensionBundleHttpExtensions.AzureRefHeaderName, headerValue);

            var result = resp.GetAzureRef();
            Assert.Equal(headerValue, result);
        }

        [Fact]
        public void GetAzureRef_TruncatesAndSanitizes()
        {
            var longVal = new string('a', 140);
            var noisy = "\t" + longVal + "\n";
            var resp = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            resp.Headers.Add(ExtensionBundleHttpExtensions.AzureRefHeaderName, noisy);

            var result = resp.GetAzureRef();
            Assert.NotNull(result);
            Assert.Equal(128, result.Length);
            Assert.DoesNotContain('\n', result);
            Assert.DoesNotContain('\t', result);
        }

        [Fact]
        public void GetAzureRef_Absent_ReturnsNull()
        {
            var resp = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            Assert.Null(resp.GetAzureRef());
        }
    }
}
