// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
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
                Assert.Contains("WEBSITE_AUTH_ENCRYPTION_KEY", ex.Message);
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
        public void ValidateTokenUsesContainerEncryptionKeyIfAvailable()
        {
            var containerEncryptionKey = TestHelpers.GenerateKeyBytes();
            var containerEncryptionStringKey = TestHelpers.GenerateKeyHexString(containerEncryptionKey);

            var websiteAuthEncryptionKey = TestHelpers.GenerateKeyBytes();
            var websiteAuthEncryptionStringKey = TestHelpers.GenerateKeyHexString(websiteAuthEncryptionKey);

            var timeStamp = DateTime.UtcNow.AddHours(1);

            Environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey, containerEncryptionStringKey);
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, websiteAuthEncryptionStringKey);

            var token = SimpleWebTokenHelper.CreateToken(timeStamp, containerEncryptionKey);
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

        public void Dispose()
        {
            // Clean up
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, string.Empty);
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey, string.Empty);
        }
    }
}
