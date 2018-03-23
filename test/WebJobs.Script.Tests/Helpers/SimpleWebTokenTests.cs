// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Security.Cryptography;

namespace Microsoft.Azure.WebJobs.Script.Tests.Helpers
{
    public static class SimpleWebTokenTests
    {
        public static string GenerateKeyHexString()
        {
            using (var aes = new AesManaged())
            {
                aes.GenerateKey();
                return BitConverter.ToString(aes.Key).Replace("-", string.Empty);
            }
        }
    }
}
