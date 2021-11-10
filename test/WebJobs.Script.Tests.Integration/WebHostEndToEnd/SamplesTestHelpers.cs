// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    public class SamplesTestHelpers
    {
        public static async Task InvokeAndValidateHttpTrigger(EndToEndTestFixture fixture, string functionName)
        {
            string functionKey = await fixture.Host.GetFunctionSecretAsync($"{functionName}");
            string uri = $"api/{functionName}?code={functionKey}&name=Mathew";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            HttpResponseMessage response = await fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);
            Assert.Equal("Hello Mathew", body);

            // verify request also succeeds with master key
            string masterKey = await fixture.Host.GetMasterKeyAsync();
            uri = $"api/{functionName}?code={masterKey}&name=Mathew";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            response = await fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);
            Assert.Equal("Hello Mathew", body);
        }

        public static async Task<HttpResponseMessage> InvokeHttpTrigger(EndToEndTestFixture fixture, string functionName)
        {
            string functionKey = await fixture.Host.GetFunctionSecretAsync($"{functionName}");
            string uri = $"api/{functionName}?code={functionKey}&name=Mathew";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            return await fixture.Host.HttpClient.SendAsync(request);
        }

        public static async Task<HttpResponseMessage> InvokeDrain(EndToEndTestFixture fixture)
        {
            string masterKey = await fixture.Host.GetMasterKeyAsync();
            string uri = $"admin/host/drain?code={masterKey}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            return await fixture.Host.HttpClient.SendAsync(request);
        }

        public static async Task<HttpResponseMessage> InvokeDrainStatus(EndToEndTestFixture fixture)
        {
            string masterKey = await fixture.Host.GetMasterKeyAsync();
            string uri = $"admin/host/drain/status?code={masterKey}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            return await fixture.Host.HttpClient.SendAsync(request);
        }

        public static async Task InvokeAndValidateHttpTriggerWithoutContentType(EndToEndTestFixture fixture, string functionName)
        {
            string functionKey = await fixture.Host.GetFunctionSecretAsync(functionName);
            string uri = $"api/{functionName}?code={functionKey}&name=Host";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            HttpResponseMessage response = await fixture.Host.HttpClient.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);
            Assert.Equal("Hello Host", body);
        }
    }
}
