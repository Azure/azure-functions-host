// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Helpers
{
    public class EncryptionHelperTests : IDisposable
    {
        [Fact]
        public void EncryptShouldThrowIdNoEncryptionKeyDefined()
        {
            // Make sure WEBSITE_AUTH_ENCRYPTION_KEY is empty
            Environment.SetEnvironmentVariable("WEBSITE_AUTH_ENCRYPTION_KEY", string.Empty);

            try
            {
                EncryptionHelper.Encrypt("value");
            }
            catch (Exception ex)
            {
                Assert.IsType<InvalidOperationException>(ex);
                Assert.Equal("No encryption key defined in the environment.", ex.Message);
            }
        }

        [Theory]
        [InlineData("value")]
        public void EncryptShouldGenerateDecryptableValues(string valueToEncrypt)
        {
            var key = TestHelpers.GenerateKeyBytes();
            var stringKey = TestHelpers.GenerateKeyHexString(key);
            Environment.SetEnvironmentVariable("WEBSITE_AUTH_ENCRYPTION_KEY", stringKey);

            var encrypted = EncryptionHelper.Encrypt(valueToEncrypt);
            var decrypted = EncryptionHelper.Decrypt(key, encrypted);

            Assert.Matches("(.*)[.](.*)[.](.*)", encrypted);
            Assert.Equal(valueToEncrypt, decrypted);
        }

        [Fact]
        public void Encrypt_And_Decrypt_Context_With_Signature()
        {
            var websiteAuthEncryptionKey = TestHelpers.GenerateKeyBytes();
            var websiteAuthEncryptionStringKey = TestHelpers.GenerateKeyHexString(websiteAuthEncryptionKey);
            var hostContext = GetHostAssignmentContext();
            var hostContextJson = JsonConvert.SerializeObject(hostContext);

            Environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey, string.Empty);
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, websiteAuthEncryptionStringKey);

            var encryptedHostContextWithSignature = EncryptionHelper.Encrypt(hostContextJson, websiteAuthEncryptionKey, includesSignature: true);

            var decryptedHostContextJson = EncryptionHelper.Decrypt(websiteAuthEncryptionKey, encryptedHostContextWithSignature);

            Assert.Equal(hostContextJson, decryptedHostContextJson);
        }

        public void Dispose()
        {
            // Clean up
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, string.Empty);
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey, string.Empty);
        }

        private static HostAssignmentContext GetHostAssignmentContext()
        {
            var hostAssignmentContext = new HostAssignmentContext();
            hostAssignmentContext.SiteId = 1;
            hostAssignmentContext.SiteName = "sitename";
            hostAssignmentContext.LastModifiedTime = DateTime.UtcNow.Add(TimeSpan.FromMinutes(new Random().Next()));
            hostAssignmentContext.Environment = new Dictionary<string, string>();
            hostAssignmentContext.MSIContext = new MSIContext();
            hostAssignmentContext.EncryptedTokenServiceSpecializationPayload = "payload";
            hostAssignmentContext.TokenServiceApiEndpoint = "endpoints";
            hostAssignmentContext.CorsSettings = new CorsSettings();
            hostAssignmentContext.EasyAuthSettings = new EasyAuthSettings();
            hostAssignmentContext.Secrets = new FunctionAppSecrets();
            hostAssignmentContext.IsWarmupRequest = false;

            return hostAssignmentContext;
        }
    }
}
