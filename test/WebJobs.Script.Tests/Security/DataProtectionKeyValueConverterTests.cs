// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Xunit;

using static Microsoft.Azure.Web.DataProtection.Constants;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class DataProtectionKeyValueConverterTests
    {
        private ScriptSettingsManager _settingsManager = ScriptSettingsManager.Instance;

        [Fact]
        public void ReadKeyValue_CanRead_WrittenKey()
        {
            var converter = new DataProtectionKeyValueConverter(FileAccess.ReadWrite);

            string keyId = Guid.NewGuid().ToString();

            using (var variables = new TestScopedSettings(_settingsManager, AzureWebsiteLocalEncryptionKey, "0F75CA46E7EBDD39E4CA6B074D1F9A5972B849A55F91A248"))
            {
                // Create our test input key
                var testInputKey = new Key { Name = "Test", Value = "Test secret value" };

                // Encrypt the key
                var resultKey = converter.WriteValue(testInputKey);

                // Decrypt the encrypted key
                Key decryptedSecret = converter.ReadValue(resultKey);

                Assert.Equal(testInputKey.Value, decryptedSecret.Value);
            }
        }

        [Fact]
        public void WriteValue_WithReadAccess_ThrowsExpectedException()
        {
            var converter = new DataProtectionKeyValueConverter(FileAccess.Read);
            Assert.Throws<InvalidOperationException>(() => converter.WriteValue(new Key()));
        }

        [Fact]
        public void ReadValue_WithWriteAccess_ThrowsExpectedException()
        {
            var converter = new DataProtectionKeyValueConverter(FileAccess.Write);
            Assert.Throws<InvalidOperationException>(() => converter.ReadValue(new Key()));
        }
    }
}