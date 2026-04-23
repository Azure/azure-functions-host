// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication.Shared
{
    /// <summary>
    /// Parses container/site encryption keys from their environment-variable form.
    /// A 64-character string is interpreted as hex (32 bytes); anything else as
    /// Base64. This file is shared (via linked source) between
    /// <c>WebJobs.Script.WebHost</c> and <c>Functions.WorkerProxy</c> so both
    /// projects decode identically.
    /// </summary>
    internal static class SiteTokenKeyParser
    {
        /// <summary>
        /// Parses the key, throwing on malformed input.
        /// </summary>
        public static byte[] ToKeyBytes(string hexOrBase64)
        {
            if (hexOrBase64.Length == 64)
            {
                return ParseHex(hexOrBase64);
            }

            return Convert.FromBase64String(hexOrBase64);
        }

        private static byte[] ParseHex(string hex)
        {
            byte[] bytes = new byte[32];
            for (int i = 0; i < 32; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return bytes;
        }
    }
}
