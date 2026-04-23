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

        [Fact]
        public void ToKeyBytes_MalformedInput_Throws()
        {
            // Guards the runtime contract: SecretsUtility.ToKeyBytes (and the
            // shared SiteTokenKeyParser it delegates to) must throw on malformed
            // input. SecretsUtility.GetTokenIssuerSigningKeys relies on this to
            // surface configuration errors at startup; silently swallowing would
            // leave operators chasing 401s with no signal.
            const string MalformedHex = "ZZ75CA46E7EBDD39E4CA6B074D1F9A5972B849A55F91A248F6B038A61BACE9D7";
            Assert.Throws<FormatException>(() => SecretsUtility.ToKeyBytes(MalformedHex));
            Assert.Throws<FormatException>(() => SecretsUtility.ToKeyBytes("not-base64-or-hex"));
        }
    }
}
