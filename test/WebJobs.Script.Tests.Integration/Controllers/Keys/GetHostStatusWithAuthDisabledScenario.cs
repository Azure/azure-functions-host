// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net;
using System.Net.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers.Keys
{
    [Trait("A GET request is made for the admin status API with the webHost initialized with IsAuthDisabled: true", "")]
    public class GetHostStatusWithAuthDisabledScenario : IClassFixture<GetHostStatusWithAuthDisabledScenario.Fixture>
    {
        private readonly Fixture _fixture;

        public GetHostStatusWithAuthDisabledScenario(Fixture fixture)
        {
            _fixture = fixture;
        }

        [Fact(DisplayName = "HTTP Response status code is 200 (OK).")]
        public void ResponseIs200OK()
        {
            Assert.Equal(HttpStatusCode.OK, _fixture.HttpResponse.StatusCode);
        }

        public class Fixture : ControllerDisabledAuthScenarioTestsFixture
        {
            private readonly string _requestUri = "http://localhost/admin/host/status";

            public Fixture()
            {
                HttpResponse = HttpClient.GetAsync(RequestUriFormat).Result;
                try
                {
                    Result = HttpResponse.Content.ReadAsAsync<HostStatus>().Result;
                }
                catch
                {
                    Result = null;
                }
            }

            public HostStatus Result { get; }

            public HttpResponseMessage HttpResponse { get; }

            protected virtual string RequestUriFormat => _requestUri;
        }
    }
}
