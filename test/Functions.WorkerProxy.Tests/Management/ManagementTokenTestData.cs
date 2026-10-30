// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Azure.Functions.Tests;

internal static class ManagementTokenTestData
{
    public const string HeaderName = "x-ms-site-token";
    public const string Issuer = "https://legion.core.azurewebsites.net";
    public const string RuntimePodName = "test-runtime-pod";
    public static readonly string WorkerKey = Convert.ToBase64String(SHA256.HashData("test-worker-key"u8));
    public static readonly string RuntimeKey = Convert.ToBase64String(SHA256.HashData("runtime-management-key"u8));
    public static readonly string AppKey = Convert.ToHexString(SHA256.HashData("management-test-app-key"u8));

    public static string Create(
        string encodedKey, string podName, DateTimeOffset? notBefore = null, DateTimeOffset? expires = null,
        string issuer = Issuer, string algorithm = "HS256")
    {
        byte[] key = encodedKey.Length == 64 ? Convert.FromHexString(encodedKey) : Convert.FromBase64String(encodedKey);
        string header = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new { alg = algorithm, typ = "JWT" }));
        string payload = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = issuer,
            aud = podName,
            nbf = (notBefore ?? DateTimeOffset.UtcNow.AddMinutes(-1)).ToUnixTimeSeconds(),
            exp = (expires ?? DateTimeOffset.UtcNow.AddMinutes(10)).ToUnixTimeSeconds()
        }));
        string content = $"{header}.{payload}";

        return $"{content}.{Base64Url.EncodeToString(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(content)))}";
    }
}
