// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Helpers
{
    public class SecretsUtilityTest
    {
        [Fact]
        public void ToKeyBytes_ReturnsExpectedValue()
        {
            byte[] keyBytes = TestHelpers.GenerateKeyBytes();

            string hexKey = TestHelpers.GenerateKeyHexString(keyBytes);
            string base64Key = Convert.ToBase64String(keyBytes);

            Assert.Equal(keyBytes, SecretsUtility.ToKeyBytes(hexKey));
            Assert.Equal(keyBytes, SecretsUtility.ToKeyBytes(base64Key));
            Assert.Equal(keyBytes, Convert.FromBase64String(base64Key));
        }
    }
}
