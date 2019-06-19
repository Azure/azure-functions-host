// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public class CustomHeadersMiddlewareCSharpEndToEndTests :
        CustomHeadersMiddlewareEndToEndTestsBase<CustomHeadersMiddlewareCSharpEndToEndTests.TestFixture>
    {
        public CustomHeadersMiddlewareCSharpEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public Task CustomHeadersMiddlewareRootUrl()
        {
            return CustomHeadersMiddlewareRootUrlTest();
        }

        [Fact]
        public Task CustomHeadersMiddlewareAdminUrl()
        {
            return CustomHeadersMiddlewareAdminUrlTest();
        }

        [Fact]
        public Task CustomHeadersMiddlewareHttpTriggerUrl()
        {
            return CustomHeadersMiddlewareHttpTriggerUrlTest();
        }

        [Fact]
        public Task CustomHeadersMiddlewareExtensionWebhookUrl()
        {
            return CustomHeadersMiddlewareExtensionWebhookUrlTest();
        }

        public class TestFixture : CustomHeadersMiddlewareTestFixture
        {
            private const string ScriptRoot = @"TestScripts\CustomHeadersMiddleware\CSharp";

            public TestFixture() : base(ScriptRoot, "csharp", LanguageWorkerConstants.DotNetLanguageWorkerName)
            {
            }
        }
    }
}
