// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Middlewares
{
    [Trait("Category", "E2E")]
    [Trait("E2E", nameof(AppServiceHeaderFixupMiddlewareEndToEnd))]
    public class AppServiceHeaderFixupMiddlewareEndToEnd : EndToEndTestsBase<AppServiceHeaderFixupMiddlewareEndToEnd.TestFixture>
    {
        public AppServiceHeaderFixupMiddlewareEndToEnd(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task HostHeaderIsCorrect()
        {
            const string actualHost = "actual-host";
            const string actualProtocol = "https";
            const string functionPath = "api/httpTriggerHeaderCheck";
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format($"http://localhost/{functionPath}")),
                Method = HttpMethod.Get
            };

            request.Headers.TryAddWithoutValidation("DISGUISED-HOST", actualHost);
            request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", actualProtocol);

            var response = await Fixture.Host.HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var url = await response.Content.ReadAsStringAsync();
            Assert.Equal($"{actualProtocol}://{actualHost}/{functionPath}", url);
        }

        public class TestFixture : EndToEndTestFixture
        {
            private const string ScriptRoot = @"TestScripts\CSharp";

            public TestFixture() : base(ScriptRoot, "middleware")
            {
            }
        }
    }
}
