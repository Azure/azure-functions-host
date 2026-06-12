// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers
{
    [Collection(HostFixture.Collection)]
    [Trait("A DELETE request is made against the host function key resource endpoint", "")]
    public class DeleteHostFunctionKeysScenario : DeleteFunctionKeysScenario, IClassFixture<DeleteHostFunctionKeysScenario.HostFixture>
    {
        private readonly Fixture _fixture;

        public DeleteHostFunctionKeysScenario(HostFixture fixture)
            : base(fixture)
        {
            _fixture = fixture;
        }

        public class HostFixture : DeleteFunctionKeysScenario.Fixture
        {
            private readonly string _requestUri = "http://localhost/admin/host/keys/TestKey";

            protected override string RequestUriFormat => _requestUri;

            public override string TestKeyScope => HostKeyScopes.FunctionKeys;

            public override ScriptSecretsType SecretsType => ScriptSecretsType.Host;
        }
    }

    [Collection(HostFixture.Collection)]
    [Trait("A DELETE request is made against the host function key (functionKeys) resource endpoint", "")]
    public class DeleteHostFunctionKeysNewEndpointScenario : DeleteFunctionKeysScenario, IClassFixture<DeleteHostFunctionKeysNewEndpointScenario.HostFixture>
    {
        private readonly Fixture _fixture;

        public DeleteHostFunctionKeysNewEndpointScenario(HostFixture fixture)
            : base(fixture)
        {
            _fixture = fixture;
        }

        public class HostFixture : DeleteFunctionKeysScenario.Fixture
        {
            private readonly string _requestUri = "http://localhost/admin/host/functionkeys/TestKey";

            protected override string RequestUriFormat => _requestUri;

            public override string TestKeyScope => HostKeyScopes.FunctionKeys;

            public override ScriptSecretsType SecretsType => ScriptSecretsType.Host;
        }
    }
}