// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SanitizerTests
    {
        [Theory]
        [InlineData("", "")]
        [InlineData(null, "")]
        [InlineData("Foo", "Foo")]
        [InlineData("@#$%J@Ifas9fh8q2u3rjwncasjhc asKJFNASDF", "@#$%J@Ifas9fh8q2u3rjwncasjhc asKJFNASDF")]
        [InlineData("TokenFoo", "TokenFoo")]
        [InlineData("PublicKeyToken=1234456789ab", "PublicKeyToken=1234456789ab")]
        [InlineData("Token=1234456789ab,Test=bar'Other=bar", "[Hidden Credential]'Other=bar")]
        [InlineData(@"Token=1234456789ab,Test=bar""Other=bar", @"[Hidden Credential]""Other=bar")]
        [InlineData("Token=1234456789ab,Test=bar<Other=bar", "[Hidden Credential]<Other=bar")]
        [InlineData("Token=1234456789ab,Test=bar'Other=barData Source=secret!", "[Hidden Credential]'Other=bar[Hidden Credential]")]
        [InlineData(@"Token=1234456789ab,Test=bar""Other=barData Source=secret!", @"[Hidden Credential]""Other=bar[Hidden Credential]")]
        [InlineData("Token=1234456789ab,Test=bar<Other=barData Source=secret!", "[Hidden Credential]<Other=bar[Hidden Credential]")]
        [InlineData("DefaultEndpointsProtocol=http", "[Hidden Credential]")]
        [InlineData("DefaultEndpointsProtocol=http://test", "[Hidden Credential]")]
        [InlineData("DefaultEndpointsProtocol=http2://test", "[Hidden Credential]")]
        [InlineData("AccountKey=heyyyyyyy", "[Hidden Credential]")]
        [InlineData("Data Source=heyyyyyyy", "[Hidden Credential]")]
        [InlineData("Server=secretsauce", "[Hidden Credential]")]
        [InlineData("Password=hunter2", "[Hidden Credential]")]
        [InlineData("pwd=hunter2", "[Hidden Credential]")]
        [InlineData("test&amp;sig=", "test[Hidden Credential]")]
        [InlineData("test&sig=", "test[Hidden Credential]")]
        [InlineData("SharedAccessKey=foo", "[Hidden Credential]")]
        [InlineData(@"Hey=AS1$@%#$%W-k2j"";SharedAccessKey=foo,Data Source=barzons,Server=bathouse'testing", @"Hey=AS1$@%#$%W-k2j"";[Hidden Credential]'testing")]
        [InlineData("test?sig=", "test[Hidden Credential]")]
        public void SanitizeString(string input, string expectedOutput)
        {
            var sanitized = Sanitizer.Sanitize(input);
            Assert.Equal(expectedOutput, sanitized);
        }

        /// <summary>
        /// Ensures our short circuit for performance using MayContainCredentials
        /// isn't inadvertently bypassing any credential tokens we add later.
        /// </summary>
        [Fact]
        public void EnsureShortCircuitSanity()
        {
            foreach (var token in Sanitizer.CredentialTokens)
            {
                Assert.True(Sanitizer.MayContainCredentials(token));
            }
        }
    }
}
