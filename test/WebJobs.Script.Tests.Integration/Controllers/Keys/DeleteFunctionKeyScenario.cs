// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers
{
    [Trait("A DELETE request is made against the function key resource endpoint", "")]
    public class DeleteFunctionKeysScenario : IClassFixture<DeleteFunctionKeysScenario.Fixture>
    {
        private readonly Fixture _fixture;

        public DeleteFunctionKeysScenario(Fixture fixture)
        {
            _fixture = fixture;
        }

        [Fact(DisplayName = "HTTP Response status code is 204 (No content).")]
        public void ResponseIs204NoContent()
        {
            Assert.Equal(System.Net.HttpStatusCode.NoContent, _fixture.HttpResponse.StatusCode);
        }

        [Fact(DisplayName = "Response body is empty.")]
        public void ResponseBodyIsEmpty()
        {
            Assert.Null(_fixture.HttpResponse.Content);
        }

        [Fact(DisplayName = "The secret manager key delete is invoked.")]
        public void FunctionKeysAreRetrievedFromSecretManager()
        {
            _fixture.SecretManagerMock.Verify(s => s.DeleteSecretAsync("TestKey", _fixture.TestFunctionName));
        }

        public class Fixture : KeyManagementFixture
        {
            private readonly string _requestUri = "http://localhost/admin/functions/{0}/keys/TestKey";

            public Fixture()
            {
                HttpClient.DefaultRequestHeaders.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, "1234");
                HttpResponse = HttpClient.DeleteAsync(FormattedRequestUri).Result;
            }

            public HttpResponseMessage HttpResponse { get; }

            public string FormattedRequestUri => string.Format(RequestUriFormat, TestFunctionName);

            protected virtual string RequestUriFormat => _requestUri;

            protected override Mock<TestSecretManager> BuildSecretManager()
            {
                var manager = base.BuildSecretManager();
                manager.Setup(s => s.DeleteSecretAsync("TestKey", TestFunctionName))
                    .ReturnsAsync(true);

                return manager;
            }
        }
    }
}
