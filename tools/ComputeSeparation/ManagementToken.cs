// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Azure.Functions.ComputeSeparation;

internal static class ManagementToken
{
    internal static string Create(string encodedKey, string podName)
    {
        byte[] key = Convert.FromHexString(encodedKey);
        string header = Base64Url.EncodeToString("""{"alg":"HS256","typ":"JWT"}"""u8);
        string payload = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = "https://legion.core.azurewebsites.net",
            aud = podName,
            nbf = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(),
            exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds()
        }));
        string content = $"{header}.{payload}";

        return $"{content}.{Base64Url.EncodeToString(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(content)))}";
    }
}
