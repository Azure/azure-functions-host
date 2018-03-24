// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers
{
    [Trait("A POST request is made against the host function key resource endpoint", "")]
    public class PostHostFunctionKeyScenario : PostFunctionKeysScenario, IClassFixture<PostHostFunctionKeyScenario.HostFixture>
    {
        public PostHostFunctionKeyScenario(HostFixture fixture)
            : base(fixture)
        {
        }

        public class HostFixture : PostFunctionKeysScenario.Fixture
        {
            private readonly string _requestUri = "http://localhost/admin/host/keys/TestKey";

            protected override string RequestUriFormat => _requestUri;
        }
    }
}