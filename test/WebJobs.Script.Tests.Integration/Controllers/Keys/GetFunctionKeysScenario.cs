// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers
{
    [Trait("A GET request is made against the function keys collection endpoint", "")]
    public class GetFunctionKeysScenario : IClassFixture<GetFunctionKeysScenario.GetKeysFixture>
    {
        private readonly GetKeysFixture _fixture;

        public GetFunctionKeysScenario(GetKeysFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact(DisplayName = "HTTP Response status code is 200 (OK).")]
        public void ResponseIs200Ok()
        {
            Assert.Equal(System.Net.HttpStatusCode.OK, _fixture.HttpResponse.StatusCode);
        }

        [Fact(DisplayName = "The function keys are retrieved from the secret manager.")]
        public void FunctionKeysAreRetrievedFromSecretManager()
        {
            _fixture.SecretManagerMock.Verify(s => s.GetFunctionSecrets(_fixture.TestFunctionName, false));
        }

        [Fact(DisplayName = "Response body is a valid list of keys.")]
        public void ResponseBodyIsValidAPIModelRepresentation()
        {
            Assert.NotNull(_fixture.Result);

            var keys = _fixture.Result["keys"].ToObject<IList<Key>>();

            Assert.Equal(_fixture.TestFunctionKeys.Count, keys.Count);

            foreach (var key in keys)
            {
                Assert.True(_fixture.TestFunctionKeys.ContainsKey(key.Name));
                Assert.Equal(_fixture.TestFunctionKeys[key.Name], key.Value);
            }
        }

        [Fact(DisplayName = "The returned resource has a valid 'self' link.")]
        public void ReturnedResourceHasValidSelfLink()
        {
            var selfLink = _fixture.Result.Links.FirstOrDefault(l => string.Compare(l.Relation, "self") == 0);

            Assert.Equal(_fixture.FormattedRequestUri, selfLink?.Href.ToString());
            Assert.Equal("self", selfLink?.Relation);
        }

        public class GetKeysFixture : KeyManagementFixture
        {
            private readonly string _requestUri = "http://localhost/admin/functions/{0}/keys";

            public GetKeysFixture()
            {
                HttpClient.DefaultRequestHeaders.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, "1234");
                HttpResponse = HttpClient.GetAsync(FormattedRequestUri).Result;
                Result = HttpResponse.Content.ReadAsAsync<ApiModel>().Result;
            }

            public ApiModel Result { get; }

            public HttpResponseMessage HttpResponse { get; }

            public string FormattedRequestUri => string.Format(RequestUriFormat, TestFunctionName);

            protected virtual string RequestUriFormat => _requestUri;
        }
    }
}
