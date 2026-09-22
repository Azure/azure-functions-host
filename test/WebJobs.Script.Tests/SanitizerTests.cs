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
        [InlineData("test?code=XPAAAAAAAAAAAAAT-ag==", "test[Hidden Credential]")]
        [InlineData("test?foo=bar&code=REAAAAAAAAAAAAAT-ag==", "test?foo=bar[Hidden Credential]")]
        [InlineData("test&amp;code=MiAAAAAAAAAAAAAAAAT-ag==", "test[Hidden Credential]")]
        [InlineData("aaa://aaa:aaaaaa1111aa@aaa.aaa.io:1111", "[Hidden Credential]")]
        [InlineData("test,aaa://aaa:aaaaaa1111aa@aaa.aaa.io:1111,test", "test,[Hidden Credential],test")]
        [InlineData(@"some text abc://abc:aaaaaa1111aa@aaa.abc.io:1111 some text abc://abc:aaaaaa1111aa@aaa.abc.io:1111 text", @"some text [Hidden Credential] some text [Hidden Credential] text")]
        [InlineData(@"some text abc://abc:aaaaaa1111aa@aaa.abc.io:1111 some text AccountKey=heyyyyyyy text", @"some text [Hidden Credential] some text [Hidden Credential]")]
        [InlineData("someone@contoso.com", "[Hidden Email]")]
        [InlineData("SOMEONE@CONTOSO.COM", "[Hidden Email]")]
        [InlineData("/api/GetLearnerProfile/someone@contoso.com", "/api/GetLearnerProfile/[Hidden Email]")]
        [InlineData("/api/GetLearnerProfile/first.last+tag@sub.contoso.co.uk/details", "/api/GetLearnerProfile/[Hidden Email]/details")]
        [InlineData("Failed to notify someone@contoso.com about the run.", "Failed to notify [Hidden Email] about the run.")]
        [InlineData("Notify a@b.com and c@d.org", "Notify [Hidden Email] and [Hidden Email]")]
        [InlineData("no email here", "no email here")]
        [InlineData("@contoso.com", "@contoso.com")]
        [InlineData("someone@localhost", "someone@localhost")]
        [InlineData("Token=1234456789ab,Test=someone@contoso.com'Other=bar", "[Hidden Credential]'Other=bar")]
        [InlineData("/api/users/someone@contoso.com?code=XPAAAAAAAAAAAAAT-ag==", "/api/users/[Hidden Email][Hidden Credential]")]
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

        /// <summary>
        /// Emails are not credentials, so they need their own short circuit. A bare email address contains
        /// neither '=' nor ':' and would never reach the email replacement otherwise.
        /// </summary>
        [Theory]
        [InlineData("someone@contoso.com", true)]
        [InlineData("/api/GetLearnerProfile/someone@contoso.com", true)]
        [InlineData("no email here", false)]
        [InlineData("Token=1234456789ab", false)]
        [InlineData("", false)]
        public void MayContainEmail_ReturnsExpectedResult(string input, bool expected)
        {
            Assert.Equal(expected, Sanitizer.MayContainEmail(input));
        }

        [Fact]
        public void EnsureEmailShortCircuitSanity()
        {
            Assert.False(Sanitizer.MayContainCredentials("someone@contoso.com"));
            Assert.True(Sanitizer.MayContainEmail("someone@contoso.com"));
        }
    }
}
