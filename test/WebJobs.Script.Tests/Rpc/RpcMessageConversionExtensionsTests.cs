// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class RpcMessageConversionExtensionsTests
    {
        [Theory]
        [InlineData("application/x-www-form-urlencoded’", "say=Hi&to=Mom")]
        public void HttpObjects_StringBody(string expectedContentType, object body)
        {
            var logger = MockNullLoggerFactory.CreateLogger();

            var headers = new HeaderDictionary();
            headers.Add("content-type", expectedContentType);
            HttpRequest request = HttpTestHelpers.CreateHttpRequest("GET", "http://localhost/api/httptrigger-scenarios", headers, body);

            var rpcRequestObject = request.ToRpc(logger);
            Assert.Equal(body.ToString(), rpcRequestObject.Http.Body.String);

            string contentType;
            rpcRequestObject.Http.Headers.TryGetValue("content-type", out contentType);
            Assert.Equal(expectedContentType, contentType);
        }

        [Theory]
        [InlineData("say=Hi&to=Mom", new string[] { "say", "to" }, new string[] { "Hi", "Mom" })]
        [InlineData("say=Hi", new string[] { "say" }, new string[] { "Hi" })]
        [InlineData("say=Hi&to=", new string[] { "say" }, new string[] { "Hi" })] // Removes empty value query params
        public void HttpObjects_Query(string queryString, string[] expectedKeys, string[] expectedValues)
        {
            var logger = MockNullLoggerFactory.CreateLogger();

            HttpRequest request = HttpTestHelpers.CreateHttpRequest("GET", $"http://localhost/api/httptrigger-scenarios?{queryString}");

            var rpcRequestObject = request.ToRpc(logger);
            // Same number of expected key value pairs
            Assert.Equal(rpcRequestObject.Http.Query.Count, expectedKeys.Length);
            Assert.Equal(rpcRequestObject.Http.Query.Count, expectedValues.Length);
            // Same key and value strings for each pair
            for (int i = 0; i < expectedKeys.Length; i++)
            {
                Assert.True(rpcRequestObject.Http.Query.ContainsKey(expectedKeys[i]));
                Assert.Equal(rpcRequestObject.Http.Query.GetValueOrDefault(expectedKeys[i]), expectedValues[i]);
            }
        }

        [Theory]
        [InlineData(BindingDirection.In, "blob", DataType.String)]
        [InlineData(BindingDirection.Out, "blob", DataType.Binary)]
        [InlineData(BindingDirection.InOut, "blob", DataType.Stream)]
        [InlineData(BindingDirection.InOut, "blob", DataType.Undefined)]
        public void ToBindingInfo_Converts_Correctly(BindingDirection bindingDirection, string type, DataType dataType)
        {
            BindingMetadata bindingMetadata = new BindingMetadata
            {
                Direction = bindingDirection,
                Type = type,
                DataType = dataType
            };

            BindingInfo bindingInfo = bindingMetadata.ToBindingInfo();

            Assert.Equal(bindingInfo.Direction, (BindingInfo.Types.Direction)bindingMetadata.Direction);
            Assert.Equal(bindingInfo.Type, bindingMetadata.Type);
            Assert.Equal(bindingInfo.DataType, (BindingInfo.Types.DataType)bindingMetadata.DataType);
        }

        [Fact]
        public void ToBindingInfo_Defaults_EmptyDataType()
        {
            BindingMetadata bindingMetadata = new BindingMetadata
            {
                Direction = BindingDirection.In,
                Type = "blob"
            };

            BindingInfo bindingInfo = bindingMetadata.ToBindingInfo();

            Assert.Equal(bindingInfo.Direction, (BindingInfo.Types.Direction)bindingMetadata.Direction);
            Assert.Equal(bindingInfo.Type, bindingMetadata.Type);
            Assert.Equal(bindingInfo.DataType, BindingInfo.Types.DataType.Undefined);
        }

        [Fact]
        public void HttpObjects_ClaimsPrincipal()
        {
            var logger = MockNullLoggerFactory.CreateLogger();

            HttpRequest request = HttpTestHelpers.CreateHttpRequest("GET", $"http://localhost/apihttptrigger-scenarios");

            var claimsIdentity1 = MockEasyAuth("facebook", "Connor McMahon", "10241897674253170");
            var claimsIdentity2 = new ClaimsIdentity(new List<Claim>
            {
                new Claim("http://schemas.microsoft.com/2017/07/functions/claims/authlevel", "Function")
            }, "WebJobsAuthLevel");
            var claimsIdentity3 = new ClaimsIdentity();
            var claimsIdentities = new List<ClaimsIdentity> { claimsIdentity1, claimsIdentity2, claimsIdentity3 };

            request.HttpContext.User = new ClaimsPrincipal(claimsIdentities);

            var rpcRequestObject = request.ToRpc(logger);

            var identities = request.HttpContext.User.Identities.ToList();
            var rpcIdentities = rpcRequestObject.Http.Identities.ToList();

            Assert.Equal(claimsIdentities.Count, rpcIdentities.Count);

            for (int i = 0; i < identities.Count; i++)
            {
                var identity = identities[i];
                var rpcIdentity = rpcIdentities.ElementAtOrDefault(i);

                Assert.NotNull(identity);
                Assert.NotNull(rpcIdentity);

                Assert.Equal(rpcIdentity.AuthenticationType?.Value, identity.AuthenticationType);
                Assert.Equal(rpcIdentity.NameClaimType?.Value, identity.NameClaimType);
                Assert.Equal(rpcIdentity.RoleClaimType?.Value, identity.RoleClaimType);

                var claims = identity.Claims.ToList();
                for (int j = 0; j < claims.Count; j++)
                {
                    Assert.True(rpcIdentity.Claims.ElementAtOrDefault(j) != null);
                    Assert.Equal(rpcIdentity.Claims[j].Type, claims[j].Type);
                    Assert.Equal(rpcIdentity.Claims[j].Value, claims[j].Value);
                }
            }
        }

        internal static ClaimsIdentity MockEasyAuth(string provider, string name, string id)
        {
            var claims = new List<Claim>
            {
                new Claim("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name", name),
                new Claim("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn", name),
                new Claim("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier", id)
            };

            var identity = new ClaimsIdentity(
                claims,
                provider,
                "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
                "http://schemas.microsoft.com/ws/2008/06/identity/claims/role");

            return identity;
        }
    }
}
