// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
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
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
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

        public static async Task<HttpResponseMessage> InvokeResume(EndToEndTestFixture fixture)
        {
            string masterKey = await fixture.Host.GetMasterKeyAsync();
            string uri = $"admin/host/resume?code={masterKey}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            return await fixture.Host.HttpClient.SendAsync(request);
        }

        public static async Task SetHostStateAsync(EndToEndTestFixture fixture, string state)
        {
            var masterKey = await fixture.Host.GetMasterKeyAsync();
            var request = new HttpRequestMessage(HttpMethod.Put, "admin/host/state");
            request.Content = new StringContent($"'{state}'");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, masterKey);
            var response = await fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(response.StatusCode, HttpStatusCode.Accepted);
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

        public static async Task<HttpResponseMessage> InvokeEndpointGet(EndToEndTestFixture fixture, string endpoint)
        {
            string masterKey = await fixture.Host.GetMasterKeyAsync();
            string uri = $"{endpoint}?code={masterKey}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            return await fixture.Host.HttpClient.SendAsync(request);
        }

        public static async Task<HttpResponseMessage> InvokeEndpointPut(EndToEndTestFixture fixture, string endpoint, object testContent)
        {
            string masterKey = await fixture.Host.GetMasterKeyAsync();
            string uri = $"{endpoint}?code={masterKey}";

            return await fixture.Host.HttpClient.PutAsJsonAsync(uri, testContent);
        }
    }
}
