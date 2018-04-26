// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Security.Cryptography;
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

        public void Dispose()
        {
            // Clean up
            // Make sure to null out WEBSITE_AUTH_ENCRYPTION_KEY
            Environment.SetEnvironmentVariable("WEBSITE_AUTH_ENCRYPTION_KEY", string.Empty);
        }
    }
}
