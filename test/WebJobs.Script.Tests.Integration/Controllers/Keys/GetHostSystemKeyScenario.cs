// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
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
    [Trait("A GET request is made against the host system key resource endpoint", "")]
    public class GetHostSystemKeyScenario : IClassFixture<GetHostSystemKeyScenario.SystemKeyFixture>
    {
        private readonly SystemKeyFixture _fixture;

        public GetHostSystemKeyScenario(SystemKeyFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact(DisplayName = "HTTP Response status code is 200 (OK).")]
        public void ResponseIs200Ok()
        {
            Assert.Equal(System.Net.HttpStatusCode.OK, _fixture.HttpResponse.StatusCode);
        }

        [Fact(DisplayName = "Response body is the expected key.")]
        public void ResponseBodyIsValidAPIModelRepresentation()
        {
            Assert.NotNull(_fixture.Result);
            Key key = _fixture.Result.ToObject<Key>();

            Assert.Equal(_fixture.KeyName, key.Name);
            Assert.Equal(_fixture.KeyValue, key.Value);
        }

        public class SystemKeyFixture : KeyManagementFixture
        {
            public override async Task InitializeAsync()
            {
                await base.InitializeAsync();

                HttpClient.DefaultRequestHeaders.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, MasterKey);
                HttpResponse = HttpClient.GetAsync($"http://localhost/admin/host/systemkeys/{KeyName}").Result;
                Result = ReadApiModelContent(HttpResponse);
            }

            public ApiModel Result { get; private set; }

            public HttpResponseMessage HttpResponse { get; private set; }

            public virtual string KeyName => "sytemtest";

            public virtual string KeyValue => "system1234";

            public string MasterKey => "1234";

            protected override Mock<TestSecretManager> BuildSecretManager()
            {
                Mock<TestSecretManager> manager = base.BuildSecretManager();

                manager.Setup(s => s.GetHostSecretsAsync())
                 .ReturnsAsync(() => new HostSecretsInfo
                 {
                     MasterKey = MasterKey,
                     SystemKeys = new Dictionary<string, string>
                     {
                         { KeyName, KeyValue }
                     }
                 });

                return manager;
            }
        }
    }
}