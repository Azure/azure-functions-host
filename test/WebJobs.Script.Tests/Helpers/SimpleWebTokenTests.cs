// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Helpers
{
    public class SimpleWebTokenTests : IDisposable
    {
        [Fact]
        public void EncryptShouldThrowIdNoEncryptionKeyDefined()
        {
            // Make sure WEBSITE_AUTH_ENCRYPTION_KEY is empty
            Environment.SetEnvironmentVariable("WEBSITE_AUTH_ENCRYPTION_KEY", string.Empty);

            try
            {
                SimpleWebTokenHelper.Encrypt("value");
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

            var encrypted = SimpleWebTokenHelper.Encrypt(valueToEncrypt);
            var decrypted = SimpleWebTokenHelper.Decrypt(key, encrypted);

            Assert.Matches("(.*)[.](.*)[.](.*)", encrypted);
            Assert.Equal(valueToEncrypt, decrypted);
        }

        [Fact]
        public void CreateTokenShouldCreateAValidToken()
        {
            var key = TestHelpers.GenerateKeyBytes();
            var stringKey = TestHelpers.GenerateKeyHexString(key);
            var timeStamp = DateTime.UtcNow;
            Environment.SetEnvironmentVariable("WEBSITE_AUTH_ENCRYPTION_KEY", stringKey);

            var token = SimpleWebTokenHelper.CreateToken(timeStamp);
            var decrypted = SimpleWebTokenHelper.Decrypt(key, token);

            Assert.Equal($"exp={timeStamp.Ticks}", decrypted);
        }

        [Fact]
        public void Validate_Token_Uses_WebSiteAuthEncryptionKey_If_Available()
        {
            var containerEncryptionKey = TestHelpers.GenerateKeyBytes();
            var containerEncryptionStringKey = TestHelpers.GenerateKeyHexString(containerEncryptionKey);

            var websiteAuthEncryptionKey = TestHelpers.GenerateKeyBytes();
            var websiteAuthEncryptionStringKey = TestHelpers.GenerateKeyHexString(websiteAuthEncryptionKey);

            var timeStamp = DateTime.UtcNow.AddHours(1);

            Environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey, containerEncryptionStringKey);
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, websiteAuthEncryptionStringKey);

            var token = SimpleWebTokenHelper.CreateToken(timeStamp, websiteAuthEncryptionKey);
            Assert.True(SimpleWebTokenHelper.TryValidateToken(token, new SystemClock()));
        }

        [Fact]
        public void Validate_Token_Uses_Website_Encryption_Key_If_Container_Encryption_Key_Not_Available()
        {
            var websiteAuthEncryptionKey = TestHelpers.GenerateKeyBytes();
            var websiteAuthEncryptionStringKey = TestHelpers.GenerateKeyHexString(websiteAuthEncryptionKey);

            var timeStamp = DateTime.UtcNow.AddHours(1);

            Environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey, string.Empty);
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, websiteAuthEncryptionStringKey);

            var token = SimpleWebTokenHelper.CreateToken(timeStamp, websiteAuthEncryptionKey);
            Assert.True(SimpleWebTokenHelper.TryValidateToken(token, new SystemClock()));
        }

        [Fact]
        public void Validate_Token_Checks_Signature_If_Signature_Is_Available()
        {
            var websiteAuthEncryptionKey = TestHelpers.GenerateKeyBytes();
            var websiteAuthEncryptionStringKey = TestHelpers.GenerateKeyHexString(websiteAuthEncryptionKey);

            var timeStamp = DateTime.UtcNow.AddHours(1);

            Environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey, string.Empty);
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, websiteAuthEncryptionStringKey);

            var token = SimpleWebTokenHelper.Encrypt($"exp={timeStamp.Ticks}", websiteAuthEncryptionKey, includesSignature: true);

            Assert.True(SimpleWebTokenHelper.TryValidateToken(token, new SystemClock()));
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

            var encryptedHostContextWithSignature = SimpleWebTokenHelper.Encrypt(hostContextJson, websiteAuthEncryptionKey, includesSignature: true);

            var decryptedHostContextJson = SimpleWebTokenHelper.Decrypt(websiteAuthEncryptionKey, encryptedHostContextWithSignature);

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
