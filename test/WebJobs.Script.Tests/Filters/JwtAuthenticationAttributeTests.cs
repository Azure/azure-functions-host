// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using Microsoft.Azure.Web.DataProtection;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.Config.ScriptSettingsManager;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;
using static Microsoft.Azure.WebJobs.Script.ScriptConstants;

namespace Microsoft.Azure.WebJobs.Script.Tests.Filters
{
    public class JwtAuthenticationAttributeTests : IDisposable
    {
        private const string TestKeyValue = "0F75CA46E7EBDD39E4CA6B074D1F9A5972B849A55F91A248";
        private const string TestAppName = "testsite";
        private TestScopedEnvironmentVariable _testEnv;

        public JwtAuthenticationAttributeTests()
        {
            var values = new Dictionary<string, string>
            {
                { "AzureWebEncryptionKey", TestKeyValue },
                { AzureWebsiteName, TestAppName }
            };
            _testEnv = new TestScopedEnvironmentVariable(values);
        }

        [Theory]
        [InlineData(nameof(HttpRequestHeader.Authorization))]
        [InlineData(nameof(HttpRequestHeader.Authorization), "https://appservice.core.azurewebsites.net", "https://testsite.azurewebsites.net")]
        [InlineData(nameof(HttpRequestHeader.Authorization), "https://appservice.core.azurewebsites.net", "https://testsite.azurewebsites.net/azurefunctions")]
        [InlineData(nameof(HttpRequestHeader.Authorization), "https://testsite.scm.azurewebsites.net", "https://testsite.azurewebsites.net")]
        [InlineData(nameof(HttpRequestHeader.Authorization), "https://testsite.scm.azurewebsites.net", "https://testsite.azurewebsites.net/azurefunctions")]
        [InlineData(nameof(HttpRequestHeader.Authorization), "https://testsite.azurewebsites.net", "https://testsite.azurewebsites.net")]
        [InlineData(ScriptConstants.SiteTokenHeaderName)]
        [InlineData(ScriptConstants.SiteTokenHeaderName, "https://appservice.core.azurewebsites.net", "https://testsite.azurewebsites.net")]
        [InlineData(ScriptConstants.SiteTokenHeaderName, "https://appservice.core.azurewebsites.net", "https://testsite.azurewebsites.net/azurefunctions")]
        [InlineData(ScriptConstants.SiteTokenHeaderName, "https://testsite.scm.azurewebsites.net", "https://testsite.azurewebsites.net")]
        [InlineData(ScriptConstants.SiteTokenHeaderName, "https://testsite.scm.azurewebsites.net", "https://testsite.azurewebsites.net/azurefunctions")]
        [InlineData(ScriptConstants.SiteTokenHeaderName, "https://testsite.azurewebsites.net", "https://testsite.azurewebsites.net")]
        public async Task AuthenticateAsync_WithValidToken_SetsAdminAuthorizationLevel(string headerName, string issuer = null, string audience = null)
        {
            issuer = issuer ?? string.Format(AdminJwtScmValidIssuerFormat, Instance.GetSetting(AzureWebsiteName));
            audience = audience ?? string.Format(AdminJwtSiteFunctionsValidAudienceFormat, Instance.GetSetting(AzureWebsiteName));

            string token = JwtGenerator.GenerateToken(issuer, audience, expires: DateTime.UtcNow.AddMinutes(10));

            await AuthenticateAsync(token, headerName, AuthorizationLevel.Admin);
        }

        [Theory]
        [InlineData(-10, null, null)] // Our default clock skew setting is 5 minutes
        [InlineData(10, "invalid", null)]
        [InlineData(10, null, "invalid")]
        public async Task AuthenticateAsync_InvalidToken_DoesNotSetAuthorizationLevel(int expiry, string issuer = null, string audience = null)
        {
            issuer = issuer ?? string.Format(AdminJwtScmValidIssuerFormat, Instance.GetSetting(AzureWebsiteName));
            audience = audience ?? string.Format(AdminJwtSiteFunctionsValidAudienceFormat, Instance.GetSetting(AzureWebsiteName));

            string token = JwtGenerator.GenerateToken(issuer, audience, expires: DateTime.UtcNow.AddMinutes(expiry), notBefore: DateTime.UtcNow.AddHours(-1));

            await AuthenticateAsync(token, "Authorization", AuthorizationLevel.Anonymous);
        }

        [Fact]
        public async Task AuthenticateAsync_WithValidToken_Base64Encoding_SetsAdminAuthorizationLevel()
        {
            string issuer = string.Format(AdminJwtScmValidIssuerFormat, Instance.GetSetting(AzureWebsiteName));
            string audience = string.Format(AdminJwtSiteFunctionsValidAudienceFormat, Instance.GetSetting(AzureWebsiteName));

            string key = SecretsUtility.GetEncryptionKeyValue();
            var signingCredentials = new SigningCredentials(new SymmetricSecurityKey(key.ToKeyBytes()), "http://www.w3.org/2001/04/xmldsig-more#hmac-sha256");
            string token = new JwtSecurityTokenHandler().CreateJwtSecurityToken(issuer: issuer, audience: audience, signingCredentials: signingCredentials, expires: DateTime.UtcNow.AddMinutes(10)).RawData;

            await AuthenticateAsync(token, ScriptConstants.SiteTokenHeaderName, AuthorizationLevel.Admin);
        }

        public async Task AuthenticateAsync(string token, string headerName, AuthorizationLevel expectedLevel)
        {
            var controllerContext = new HttpControllerContext()
            {
                Request = new HttpRequestMessage(HttpMethod.Get, new Uri("http://localhost/admin/test"))
            };

            if (string.Compare(nameof(HttpRequestHeader.Authorization), headerName) == 0)
            {
                controllerContext.Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                controllerContext.Request.Headers.Add(ScriptConstants.SiteTokenHeaderName, token);
            }

            var actionContext = new HttpActionContext()
            {
                ControllerContext = controllerContext
            };

            var attribute = new JwtAuthenticationAttribute();
            await attribute.AuthenticateAsync(new HttpAuthenticationContext(actionContext, null), CancellationToken.None);

            Assert.Equal(expectedLevel, controllerContext.Request.GetAuthorizationLevel());
        }

        public void Dispose()
        {
            _testEnv.Dispose();
        }
    }
}
