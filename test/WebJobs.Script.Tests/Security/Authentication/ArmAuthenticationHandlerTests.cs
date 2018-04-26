// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Tests.Helpers;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Security.Authentication
{
    public class ArmAuthenticationHandlerTests
    {
        private static DefaultHttpContext GetContext()
        {
            var services = new ServiceCollection().AddLogging();
            services.AddAuthentication(o =>
            {
                o.DefaultScheme = ArmAuthenticationDefaults.AuthenticationScheme;
            })
            .AddArmToken();

            var sp = services.BuildServiceProvider();
            var context = new DefaultHttpContext
            {
                RequestServices = sp
            };
            return context;
        }

        [Fact]
        public async Task AuthenticateAsync_WithoutToken_DoesNotAuthenticate()
        {
            // Arrange
            DefaultHttpContext context = GetContext();

            // Act
            AuthenticateResult result = await context.AuthenticateAsync();

            // Assert
            Assert.True(result.None);
            Assert.Null(result.Failure);
            Assert.Null(result.Principal);
        }

        [Fact]
        public async Task AuthenticateAsync_WithToken_PerformsAuthentication()
        {
            using (new TestScopedEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, TestHelpers.GenerateKeyHexString()))
            {
                // Arrange
                DefaultHttpContext context = GetContext();

                string token = SimpleWebTokenHelper.CreateToken(DateTime.UtcNow.AddMinutes(2));
                context.Request.Headers.Add(ArmAuthenticationHandler.ArmTokenHeaderName, token);

                // Act
                AuthenticateResult result = await context.AuthenticateAsync();

                // Assert
                Assert.True(result.Succeeded);
                Assert.True(result.Principal.Identity.IsAuthenticated);
                Assert.True(result.Principal.HasClaim(SecurityConstants.AuthLevelClaimType, AuthorizationLevel.Admin.ToString()));
            }
        }

        [Fact]
        public async Task AuthenticateAsync_WithInvalidToken_FailsAuthentication()
        {
            using (new TestScopedEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, TestHelpers.GenerateKeyHexString()))
            {
                // Arrange
                DefaultHttpContext context = GetContext();

                string token = SimpleWebTokenHelper.CreateToken(DateTime.UtcNow.AddMinutes(2));
                token = token.Substring(0, token.Length - 5);
                context.Request.Headers.Add(ArmAuthenticationHandler.ArmTokenHeaderName, token);

                // Act
                AuthenticateResult result = await context.AuthenticateAsync();

                // Assert
                Assert.False(result.Succeeded);
                Assert.NotNull(result.Failure);
                Assert.Null(result.Principal);
            }
        }

        [Fact]
        public async Task AuthenticateAsync_WithExpiredToken_FailsAuthentication()
        {
            using (new TestScopedEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, TestHelpers.GenerateKeyHexString()))
            {
                DefaultHttpContext context = GetContext();

                string token = SimpleWebTokenHelper.CreateToken(DateTime.UtcNow.AddMinutes(-20));
                context.Request.Headers.Add(ArmAuthenticationHandler.ArmTokenHeaderName, token);

                // Act
                AuthenticateResult result = await context.AuthenticateAsync();

                // Assert
                Assert.False(result.Succeeded);
                Assert.NotNull(result.Failure);
                Assert.Null(result.Principal);
            }
        }
    }
}
