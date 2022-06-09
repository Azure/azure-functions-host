// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Security.Utilities;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security
{
    /// <summary>
    /// Generates identifiable secret values for use as Azure Functions keys.
    /// </summary>
    public static class SecretGenerator
    {
        public const string AzureFunctionsSignature = "AzFu";

        // Seeds (passed to the Marvin checksum algorithm) for grouping
        // Azure Functions Host keys. See references from unit tests for
        // information on how these seeds were generated/are versioned.
        public const ulong MasterKeySeed = 0x4d61737465723030;
        public const ulong SystemKeySeed = 0x53797374656d3030;
        public const ulong FunctionKeySeed = 0x46756e6374693030;

        public static string GenerateMasterKeyValue()
        {
            return GenerateIdentifiableSecret(MasterKeySeed);
        }

        public static string GenerateFunctionKeyValue()
        {
            return GenerateIdentifiableSecret(FunctionKeySeed);
        }

        public static string GenerateSystemKeyValue()
        {
            return GenerateIdentifiableSecret(SystemKeySeed);
        }

        internal static string GenerateIdentifiableSecret(ulong seed)
        {
            // Return a generated secret with a completely URL-safe base64-encoding
            // alphabet, 'a-zA-Z0-9' as well as '-' and '_'. We preserve the trailing
            // equal sign padding in the token in order to improve the ability to
            // match against the general token format (with performing a checksum
            // validation. This is safe in Azure Functions utilization because a
            // token will only appear in a URL as a query string parameter, where
            // equal signs do not require encoding.
            return IdentifiableSecrets.GenerateUrlSafeBase64Key(seed, 40, AzureFunctionsSignature, elidePadding: false);
        }
    }
}
