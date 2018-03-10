// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers
{
    [Trait("A GET request is made against the host function keys collection endpoint", "")]
    public class GetHostFunctionKeysScenario : GetFunctionKeysScenario, IClassFixture<GetHostFunctionKeysScenario.HostFixture>
    {
        public GetHostFunctionKeysScenario(GetKeysFixture fixture)
            : base(fixture)
        {
        }

        public class HostFixture : GetKeysFixture
        {
            private readonly string _requestUri = "http://localhost/admin/host/keys";

            protected override string RequestUriFormat => _requestUri;
        }
    }
}