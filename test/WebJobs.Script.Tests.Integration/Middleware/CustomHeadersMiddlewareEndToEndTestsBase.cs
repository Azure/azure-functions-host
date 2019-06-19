// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Models;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public abstract class CustomHeadersMiddlewareEndToEndTestsBase<TTestFixture> :
        EndToEndTestsBase<TTestFixture> where TTestFixture : CustomHeadersMiddlewareTestFixture, new()
    {
        public CustomHeadersMiddlewareEndToEndTestsBase(TTestFixture fixture) : base(fixture)
        {
        }

        protected async Task CustomHeadersMiddlewareRootUrlTest()
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, string.Empty);

            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            IEnumerable<string> values;
            Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out values));
            Assert.Equal("nosniff", values.FirstOrDefault());
        }

        protected async Task CustomHeadersMiddlewareAdminUrlTest()
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "admin/host/ping");

            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            IEnumerable<string> values;
            Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out values));
            Assert.Equal("nosniff", values.FirstOrDefault());
        }

        protected async Task CustomHeadersMiddlewareHttpTriggerUrlTest()
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "api/HttpTrigger");

            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            IEnumerable<string> values;
            Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out values));
            Assert.Equal("nosniff", values.FirstOrDefault());
        }

        protected async Task CustomHeadersMiddlewareExtensionWebhookUrlTest()
        {
            var secrets = await Fixture.Host.SecretManager.GetHostSecretsAsync();
            var url = $"/runtime/webhooks/durableTask/instances?taskHub=MiddlewareTestHub&code={secrets.MasterKey}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);

            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            IEnumerable<string> values;
            Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out values));
            Assert.Equal("nosniff", values.FirstOrDefault());
        }
    }

    public abstract class CustomHeadersMiddlewareTestFixture: EndToEndTestFixture
    {
        protected override ExtensionPackageReference[] GetExtensionsToInstall()
        {
            return new ExtensionPackageReference[]
            {
                new ExtensionPackageReference
                {
                    Id = "Microsoft.Azure.WebJobs.Extensions.DurableTask",
                    Version = "1.8.2"
                }
            };
        }

        protected CustomHeadersMiddlewareTestFixture(string rootPath, string testId, string language) :
            base(rootPath, testId, language)
        {
        }
    }
}
