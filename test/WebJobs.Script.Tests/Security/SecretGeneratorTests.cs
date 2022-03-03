// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Text;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Security.Utilities;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Security
{
    public class SecretGeneratorTests
    {
        [Fact]
        public void GenerateIdentifiableSecret_HonorsSeed()
        {
            ulong seed = uint.MaxValue;
            string secret = SecretGenerator.GenerateIdentifiableSecret(uint.MaxValue);
            ValidateSecret(secret, seed);
        }

        [Fact]
        public void GenerateMasterKeyValue_GeneratesCorrectSecret()
        {
            // This is how the preliminary system key seed was generated.
            // "Master01" can be used as the next string literal if this seed
            // is versioned. The bytes are reversed in an attempt to retain a
            // common prefix (left-hand data value) for the the seed if changed.
            ulong expectedSeed = BitConverter.ToUInt64(Encoding.ASCII.GetBytes("Master00").Reverse().ToArray());
            Assert.Equal(expectedSeed, SecretGenerator.MasterKeySeed);

            string secret = SecretGenerator.GenerateMasterKeyValue();
            ValidateSecret(secret, SecretGenerator.MasterKeySeed);
        }

        [Fact]
        public void GenerateSystemKeyValue_GeneratesCorrectSecret()
        {
            // This is how the preliminary system key seed was generated.
            // "System01" can be used as the next string literal if this seed
            // is versioned. The bytes are reversed in an attempt to retain a
            // common prefix (left-hand data value) for the the seed if changed.
            ulong expectedSeed = BitConverter.ToUInt64(Encoding.ASCII.GetBytes("System00").Reverse().ToArray());
            Assert.Equal(expectedSeed, SecretGenerator.SystemKeySeed);

            string secret = SecretGenerator.GenerateSystemKeyValue();
            ValidateSecret(secret, SecretGenerator.SystemKeySeed);
        }

        [Fact]
        public void GenerateFunctionKeyValue_GeneratesCorrectSecret()
        {
            // This is how the preliminary system key seed was generated.
            // "Functi01" can be used as the next string literal if this seed
            // is versioned. The bytes are reversed in an attempt to retain a
            // common prefix (left-hand data value) for the the seed if changed.
            ulong expectedSeed = BitConverter.ToUInt64(Encoding.ASCII.GetBytes("Functi00").Reverse().ToArray());
            Assert.Equal(expectedSeed, SecretGenerator.FunctionKeySeed);

            string secret = SecretGenerator.GenerateFunctionKeyValue();
            ValidateSecret(secret, SecretGenerator.FunctionKeySeed);
        }

        internal static void ValidateSecret(string secret, ulong seed)
        {
            Assert.True(IdentifiableSecrets.ValidateBase64Key(secret,
                                                              seed,
                                                              SecretGenerator.AzureFunctionsSignature,
                                                              encodeForUrl: true));

            // Strictly speaking, these tests shouldn't be required, failure
            // would indicate a bug in the Microsoft.Security.Utilities API itself
            // Still, this is a new dependency, so we'll do some sanity checking.

            // Azure Function secrets are base64-encoded using a URL friendly character set.
            // These tokens therefore never include the '+' or '/' characters.
            Assert.False(secret.Contains('+'));
            Assert.False(secret.Contains('/'));

            // All Azure function keys are 40 bytes in length, 56 base64-encoded chars.
            Assert.True(secret.Length == 56);
            Assert.True(Base64UrlEncoder.DecodeBytes(secret).Length == 40);

            ulong[] testSeeds = new[]
            {
                uint.MinValue, uint.MaxValue, SecretGenerator.SystemKeySeed,
                SecretGenerator.MasterKeySeed, SecretGenerator.FunctionKeySeed,
            };

            // Verify that validation fails for incorrect seed values.
            foreach (ulong testSeed in testSeeds)
            {
                if (testSeed == seed)
                {
                    // We looked at this one already.
                    continue;
                }

                Assert.False(IdentifiableSecrets.ValidateBase64Key(secret,
                                                                  testSeed,
                                                                  SecretGenerator.AzureFunctionsSignature,
                                                                  encodeForUrl: true));
            }

            // Validate that validation fails for an incorrect signature.
            Assert.False(IdentifiableSecrets.ValidateBase64Key(secret,
                                                  seed,
                                                  "XXXX",
                                                  encodeForUrl: true));
        }
    }
}
