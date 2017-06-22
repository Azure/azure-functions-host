// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
#if SCENARIOS

using System.Net;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers.Keys
{
    [Trait("Requests are made with IsAuthDisabled: true", "")]
    public class AuthDisabledScenario : IClassFixture<AuthDisabledScenario.Fixture>
    {
        private readonly Fixture _fixture;

        public AuthDisabledScenario(Fixture fixture)
        {
            _fixture = fixture;
        }

        [Fact(DisplayName = "Host Status returns 200 (OK).")]
        public void HostStatus_Returns200()
        {
            var response = _fixture.HttpClient.GetAsync("http://localhost/admin/host/status").Result;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact(DisplayName = "Function Invoke returns 200 (OK).")]
        public void FunctionInvoke_Returns200()
        {
            var response = _fixture.HttpClient.GetAsync("http://localhost/api/httptrigger-csharp?name=Mathew").Result;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        public class Fixture : ControllerDisabledAuthScenarioTestsFixture
        {
        }
    }
}
#endif