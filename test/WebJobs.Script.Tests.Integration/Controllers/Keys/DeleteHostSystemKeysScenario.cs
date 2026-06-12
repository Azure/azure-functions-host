// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers
{
    [Collection(Fixture.Collection)]
    [Trait("A DELETE request is made against the host system keys resource endpoint", "")]
    public class DeleteHostSystemKeysScenario : DeleteFunctionKeysScenario, IClassFixture<DeleteHostSystemKeysScenario.HostFixture>
    {
        private readonly Fixture _fixture;

        public DeleteHostSystemKeysScenario(HostFixture fixture)
            : base(fixture)
        {
            _fixture = fixture;
        }

        public class HostFixture : DeleteFunctionKeysScenario.Fixture
        {
            private readonly string _requestUri = "http://localhost/admin/host/systemkeys/TestKey";

            protected override string RequestUriFormat => _requestUri;

            public override string TestKeyScope => HostKeyScopes.SystemKeys;

            public override ScriptSecretsType SecretsType => ScriptSecretsType.Host;
        }
    }
}