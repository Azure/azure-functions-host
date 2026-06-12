// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers
{
    [Collection(HostFixture.Collection)]
    [Trait("A POST request is made against the host system keys resource endpoint", "")]
    public class PostHostSystemKeyScenario : PostFunctionKeysScenario, IClassFixture<PostHostSystemKeyScenario.HostFixture>
    {
        public PostHostSystemKeyScenario(HostFixture fixture)
            : base(fixture)
        {
        }

        public class HostFixture : PostFunctionKeysScenario.Fixture
        {
            private readonly string _requestUri = "http://localhost/admin/host/systemkeys/TestKey";

            protected override string RequestUriFormat => _requestUri;
        }
    }
}