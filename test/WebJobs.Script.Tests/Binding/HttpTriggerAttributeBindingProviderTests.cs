// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net.Http;
using Microsoft.Azure.WebJobs.Script.Binding;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class HttpTriggerAttributeBindingProviderTests
    {
        [Fact]
        public static void HttpTriggerBinding_ToInvokeString_ReturnsExpectedResult()
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://functions.azurewebsites.net/api/httptrigger?code=123&name=Mathew");
            request.Headers.Add("Custom1", "Testing");

            string result = HttpTriggerAttributeBindingProvider.HttpTriggerBinding.ToInvokeString(request);
            Assert.Equal("Method: GET, Uri: https://functions.azurewebsites.net/api/httptrigger", result);
        }
    }
}
