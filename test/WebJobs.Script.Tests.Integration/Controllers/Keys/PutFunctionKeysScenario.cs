// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers
{
    [Trait("A PUT request is made against the function key resource endpoint", "")]
    public class PutFunctionKeysScenario : IClassFixture<PutFunctionKeysScenario.Fixture>
    {
        private readonly Fixture _fixture;

        public PutFunctionKeysScenario(Fixture fixture)
        {
            _fixture = fixture;
        }

        [Fact(DisplayName = "HTTP Response status code is 201 (Created).")]
        public void ResponseIs201Ok()
        {
            Assert.Equal(System.Net.HttpStatusCode.Created, _fixture.HttpResponse.StatusCode);
        }

        [Fact(DisplayName = "The Location header is set.")]
        public void LocationIsSet()
        {
            Assert.Equal(_fixture.FormattedRequestUri, _fixture.HttpResponse.Headers.Location.ToString());
        }

        [Fact(DisplayName = "Response body is the expected key.")]
        public void ResponseBodyIsValidAPIModelRepresentation()
        {
            Assert.NotNull(_fixture.Result);
            Key key = _fixture.Result.ToObject<Key>();

            Assert.Equal("TestKey", key.Name);
            Assert.Equal("testvalue", key.Value);
        }

        [Fact(DisplayName = "The returned resource has a valid 'self' link.")]
        public void ReturnedResourceHasValidSelfLink()
        {
            var selfLink = _fixture.Result.Links.FirstOrDefault(l => string.Compare(l.Relation, "self") == 0);

            Assert.Equal(_fixture.FormattedRequestUri, selfLink?.Href.ToString());
            Assert.Equal("self", selfLink?.Relation);
        }

        public class Fixture : KeyManagementFixture
        {
            private readonly string _requestUri = "http://localhost/admin/functions/{0}/keys/TestKey";

            public override async Task InitializeAsync()
            {
                await base.InitializeAsync();

                HttpClient.DefaultRequestHeaders.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, "1234");
                HttpResponse = HttpClient.PutAsJsonAsync(FormattedRequestUri, new { name = "TestKey", value = "testvalue" }).Result;
                Result = ReadApiModelContent(HttpResponse);
            }

            public ApiModel Result { get; private set;}

            public HttpResponseMessage HttpResponse { get; private set; }

            public string FormattedRequestUri => string.Format(RequestUriFormat, TestKeyScope);

            protected virtual string RequestUriFormat => _requestUri;
        }
    }
}