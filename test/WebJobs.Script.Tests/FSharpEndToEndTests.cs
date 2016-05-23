﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait("Category", "FSharp-E2E")]
    public class FSharpEndToEndTests : EndToEndTestsBase<FSharpEndToEndTests.TestFixture>
    {
        public FSharpEndToEndTests(TestFixture fixture)
            : base(fixture)
        {
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\FSharp", "fsharp")
            {
            }
        }
    }
}
