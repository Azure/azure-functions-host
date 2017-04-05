// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using Microsoft.Azure.Web.DataProtection;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.Config.ScriptSettingsManager;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;
using static Microsoft.Azure.WebJobs.Script.ScriptConstants;

namespace Microsoft.Azure.WebJobs.Script.Tests.Filters
{
    public class JwtAuthenticationAttributeTests
    {
        private const string TestKeyValue = "0F75CA46E7EBDD39E4CA6B074D1F9A5972B849A55F91A248";

        [Fact]
        public async Task AuthenticateAsync_WithValidToken_SetsAdminAuthorizationLevel()
        {
            Tuple<string, string> identifiers = GetTokenIdentifiers();

            await AuthenticateAsync(identifiers.Item1, identifiers.Item2, DateTime.UtcNow.AddHours(1), AuthorizationLevel.Admin);
        }

        [Fact]
        public async Task AuthenticateAsync_WithInvalidToken_DoesNotSetAuthorizationLevel()
        {
            Tuple<string, string> identifiers = GetTokenIdentifiers();

            await AuthenticateAsync(identifiers.Item1,
                identifiers.Item2,
                DateTime.UtcNow.AddMinutes(-10), // Our default clock skew setting is 5 minutes,
                AuthorizationLevel.Anonymous);
        }

        private static Tuple<string, string> GetTokenIdentifiers()
        {
            string testAudience = string.Format(AdminJwtValidAudienceFormat, Instance.GetSetting(AzureWebsiteName));
            string testIssuer = string.Format(AdminJwtValidIssuerFormat, Instance.GetSetting(AzureWebsiteName));

            return Tuple.Create(testIssuer, testAudience);
        }

        public async Task AuthenticateAsync(string issuer, string audience, DateTime expiration, AuthorizationLevel expectedLevel)
        {
            using (var tempEnvironment = new TestScopedEnvironmentVariable("AzureWebEncryptionKey", TestKeyValue))
            {
                // Create an expired test token
                var token = JwtGenerator.GenerateToken(issuer, audience, notBefore: DateTime.UtcNow.AddHours(-1), expires: expiration);

                var controllerContext = new HttpControllerContext()
                {
                    Request = new HttpRequestMessage(HttpMethod.Get, new Uri("http://localhost/admin/test"))
                };

                controllerContext.Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var actionContext = new HttpActionContext()
                {
                    ControllerContext = controllerContext
                };

                var attribute = new JwtAuthenticationAttribute();
                await attribute.AuthenticateAsync(new HttpAuthenticationContext(actionContext, null), CancellationToken.None);

                Assert.Equal(expectedLevel, controllerContext.Request.GetAuthorizationLevel());
            }
        }
    }
}
