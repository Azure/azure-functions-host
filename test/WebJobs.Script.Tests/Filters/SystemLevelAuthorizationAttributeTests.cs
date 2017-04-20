// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SystemLevelAuthorizationAttributeTests : AuthorizationLevelAttributeTests
    {
        [Theory]
        [InlineData(TestHostFunctionKeyName1, TestHostFunctionKeyValue1, AuthorizationLevel.Function, null)]
        [InlineData(TestHostFunctionKeyName2, TestHostFunctionKeyValue2, AuthorizationLevel.Function, null)]
        [InlineData(TestFunctionKeyName1, TestFunctionKeyValue1, AuthorizationLevel.Function, "TestFunction")]
        [InlineData(null, TestFunctionKeyValue1, AuthorizationLevel.Function, "TestFunction")]
        [InlineData(TestFunctionKeyName2, TestFunctionKeyValue2, AuthorizationLevel.Function, "TestFunction")]
        [InlineData(null, TestFunctionKeyValue2, AuthorizationLevel.Function, "TestFunction")]
        [InlineData("", TestMasterKeyValue, AuthorizationLevel.Admin, null)]
        [InlineData(TestSystemKeyName1, TestSystemKeyValue1, AuthorizationLevel.System, null)]
        [InlineData(TestSystemKeyName2, TestSystemKeyValue2, AuthorizationLevel.System, null)]
        [InlineData("foo", TestSystemKeyValue1, AuthorizationLevel.Anonymous, null)]
        [InlineData(TestSystemKeyName1, "bar", AuthorizationLevel.Anonymous, null)]
        public async Task GetAuthorizationLevel_ValidCodeQueryParam_WithNamedKeyRequirement_ReturnsExpectedLevel(string keyName, string keyValue, AuthorizationLevel expectedLevel, string functionName = null)
        {
            Uri uri = new Uri(string.Format("http://functions/api/foo?code={0}", keyValue));
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            Func<IDictionary<string, string>, string, bool> evaluator = (secrets, value) => SystemAuthorizationLevelAttribute.EvaluateKeyMatch(secrets, value, keyName);

            AuthorizationLevel level = await AuthorizationLevelAttribute.GetAuthorizationLevelAsync(request, MockSecretManager.Object, evaluator, functionName: functionName);

            Assert.Equal(expectedLevel, level);
        }

        [Fact]
        public async Task OnAuthorization_WithNamedKeyHeader_Succeeds()
        {
            var attribute = new SystemAuthorizationLevelAttribute(TestSystemKeyName1);

            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, TestSystemKeyValue1);
            var actionContext = CreateActionContext(typeof(SystemTestController).GetMethod(nameof(SystemTestController.Get)), HttpConfig);
            actionContext.ControllerContext.Request = request;

            await attribute.OnAuthorizationAsync(actionContext, CancellationToken.None);

            Assert.Null(actionContext.Response);
        }

        public class SystemTestController : ApiController
        {
            public void Get()
            {
            }
        }
    }
}
