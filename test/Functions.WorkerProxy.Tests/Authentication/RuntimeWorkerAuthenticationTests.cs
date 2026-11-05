// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class RuntimeWorkerAuthenticationTests
{
    private const string Audience = "test-worker-pod";
    private const string ValidHeader = """{"alg":"HS256","typ":"JWT"}""";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1762275600);

    [Theory]
    [InlineData("missing", nameof(AuthenticationFailureReason.MissingToken))]
    [InlineData("parts", nameof(AuthenticationFailureReason.MalformedToken))]
    [InlineData("too-long", nameof(AuthenticationFailureReason.MalformedToken))]
    [InlineData("signature", nameof(AuthenticationFailureReason.Signature))]
    [InlineData("signature-base64", nameof(AuthenticationFailureReason.MalformedToken))]
    [InlineData("header-base64", nameof(AuthenticationFailureReason.MalformedToken))]
    [InlineData("header-json", nameof(AuthenticationFailureReason.MalformedToken))]
    [InlineData("header-array", nameof(AuthenticationFailureReason.MalformedToken))]
    [InlineData("header-duplicate", nameof(AuthenticationFailureReason.MalformedToken))]
    [InlineData("header-type", nameof(AuthenticationFailureReason.MalformedToken))]
    [InlineData("header-critical", nameof(AuthenticationFailureReason.MalformedToken))]
    [InlineData("header-b64", nameof(AuthenticationFailureReason.MalformedToken))]
    [InlineData("header-algorithm", nameof(AuthenticationFailureReason.Algorithm))]
    [InlineData("payload-json", nameof(AuthenticationFailureReason.MalformedToken))]
    [InlineData("payload-array", nameof(AuthenticationFailureReason.MalformedToken))]
    [InlineData("payload-duplicate", nameof(AuthenticationFailureReason.MalformedToken))]
    [InlineData("payload-depth", nameof(AuthenticationFailureReason.MalformedToken))]
    public void BothContracts_RejectInvalidSignedStructure(string scenario, string expected)
    {
        string header = scenario switch
        {
            "header-json" => "{",
            "header-array" => "[]",
            "header-duplicate" => """{"alg":"HS256","typ":"JWT","typ":"JWT"}""",
            "header-type" => """{"alg":"HS256","typ":"jwt"}""",
            "header-critical" => """{"alg":"HS256","typ":"JWT","crit":[]}""",
            "header-b64" => """{"alg":"HS256","typ":"JWT","b64":true}""",
            "header-algorithm" => """{"alg":"HS512","typ":"JWT"}""",
            _ => ValidHeader
        };
        string payload = scenario switch
        {
            "payload-json" => "{",
            "payload-array" => "[]",
            "payload-duplicate" => """{"aud":"test-worker-pod","aud":"test-worker-pod"}""",
            "payload-depth" => """{"nested":[[[[[[[[[]]]]]]]]]}""",
            _ => JsonSerializer.Serialize(CreateClaims())
        };
        string? token = scenario switch
        {
            "missing" => null,
            "parts" => "only.two",
            "too-long" => new string('A', 4097),
            "signature" => Sign(header, payload, new byte[32]),
            "signature-base64" => "header.payload.%",
            "header-base64" => SignParts("%", Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload))),
            _ => Sign(header, payload)
        };

        Assert.False(RuntimeWorkerAuthentication.ValidateManagementToken(
            token, RuntimeAuthenticationTestData.WorkerKey, Audience, Now, out AuthenticationFailureReason managementReason));
        Assert.False(RuntimeWorkerAuthentication.ValidateToken(
            token, RuntimeAuthenticationTestData.WorkerKey, Audience, RuntimeWorkerAuthentication.WorkerPurpose, Now,
            out AuthenticationFailureReason runtimeReason));
        Assert.Equal(expected, managementReason.ToString());
        Assert.Equal(expected, runtimeReason.ToString());
    }

    [Theory]
    [InlineData("platform-lifetime", true, false)]
    [InlineData("management-skew", true, false)]
    [InlineData("future-within-management-skew", true, false)]
    [InlineData("audience-case", true, false)]
    [InlineData("audience-array", true, false)]
    [InlineData("missing-iat", true, false)]
    [InlineData("missing-nbf", true, false)]
    [InlineData("missing-purpose", true, false)]
    [InlineData("wrong-purpose", true, false)]
    [InlineData("wrong-issuer", false, true)]
    [InlineData("missing-issuer", false, true)]
    [InlineData("issuer-case", true, true)]
    public void Contracts_KeepIndependentClaimAndLifetimeRules(string scenario, bool managementValid, bool runtimeValid)
    {
        Dictionary<string, object> claims = CreateClaims();
        long now = Now.ToUnixTimeSeconds();
        switch (scenario)
        {
            case "platform-lifetime":
                claims["iat"] = claims["nbf"] = now - 60;
                claims["exp"] = now + 600;
                break;
            case "management-skew":
                claims["iat"] = claims["nbf"] = now - 240;
                claims["exp"] = now - 60;
                break;
            case "future-within-management-skew":
                claims["iat"] = claims["nbf"] = now + 60;
                claims["exp"] = now + 300;
                break;
            case "audience-case":
                claims["aud"] = Audience.ToUpperInvariant();
                break;
            case "audience-array":
                claims["aud"] = new[] { Audience };
                break;
            case "missing-iat":
                claims.Remove("iat");
                break;
            case "missing-nbf":
                claims.Remove("nbf");
                break;
            case "missing-purpose":
                claims.Remove("purpose");
                break;
            case "wrong-purpose":
                claims["purpose"] = RuntimeWorkerAuthentication.AppPurpose;
                break;
            case "wrong-issuer":
                claims["iss"] = "https://not-the-management-issuer";
                break;
            case "missing-issuer":
                claims.Remove("iss");
                break;
            case "issuer-case":
                claims["iss"] = RuntimeWorkerAuthentication.ManagementIssuer.ToUpperInvariant();
                break;
        }
        string token = Sign(ValidHeader, JsonSerializer.Serialize(claims));

        Assert.Equal(managementValid, RuntimeWorkerAuthentication.ValidateManagementToken(
            token, RuntimeAuthenticationTestData.WorkerKey, Audience, Now));
        Assert.Equal(runtimeValid, RuntimeWorkerAuthentication.ValidateToken(
            token, RuntimeAuthenticationTestData.WorkerKey, Audience, RuntimeWorkerAuthentication.WorkerPurpose, Now));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeyRoles_AcceptTheSamePlatformEncodings(bool hex)
    {
        byte[] expected = RuntimeAuthenticationTestData.WorkerKey;
        string encoded = hex ? Convert.ToHexString(expected) : Convert.ToBase64String(expected);

        Assert.True(RuntimeWorkerAuthentication.TryDecodeWorkerKey(encoded, out byte[] workerKey));
        Assert.True(RuntimeWorkerAuthentication.TryDecodeAppKey(encoded, out byte[] appKey));
        Assert.Equal(expected, workerKey);
        Assert.Equal(expected, appKey);
    }

    [Fact]
    public void AppProof_RetainsExactWorkerIdentityAndPurposeBinding()
    {
        string proof = RuntimeWorkerAuthentication.CreateAppProof(
            RuntimeAuthenticationTestData.AppKey, "mixed-Case-App", Audience, Now);

        Assert.True(RuntimeWorkerAuthentication.ValidateAppProof(
            proof, RuntimeAuthenticationTestData.AppKey, "MIXED-case-app", Audience, Now));
        Assert.False(RuntimeWorkerAuthentication.ValidateAppProof(
            proof, RuntimeAuthenticationTestData.AppKey, "mixed-case-app", Audience.ToUpperInvariant(), Now));
        Assert.False(RuntimeWorkerAuthentication.ValidateToken(
            proof, RuntimeAuthenticationTestData.AppKey, RuntimeWorkerAuthentication.AppAudience("mixed-case-app"),
            RuntimeWorkerAuthentication.WorkerPurpose, Now));
    }

    [Theory]
    [InlineData("exp", "\"1762275900\"")]
    [InlineData("exp", "1762275900.5")]
    [InlineData("exp", "9223372036854775808")]
    [InlineData("nbf", "\"1762275600\"")]
    [InlineData("nbf", "1762275600.5")]
    [InlineData("iat", "\"1762275600\"")]
    public void DirectionalToken_RejectsCoercedOrOutOfRangeNumericDates(string claim, string value)
    {
        string payload = JsonSerializer.Serialize(CreateClaims());
        using JsonDocument document = JsonDocument.Parse(payload);
        string original = document.RootElement.GetProperty(claim).GetRawText();
        payload = payload.Replace($"\"{claim}\":{original}", $"\"{claim}\":{value}", StringComparison.Ordinal);
        string token = Sign(ValidHeader, payload);

        Assert.False(RuntimeWorkerAuthentication.ValidateToken(
            token, RuntimeAuthenticationTestData.WorkerKey, Audience, RuntimeWorkerAuthentication.WorkerPurpose, Now));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignedTokens_ValidateWithTheStandardLibraryEngine(bool proof)
    {
        byte[] key = proof ? RuntimeAuthenticationTestData.AppKey : RuntimeAuthenticationTestData.WorkerKey;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string audience = proof ? RuntimeWorkerAuthentication.AppAudience("app") : RuntimeWorkerAuthentication.WorkerAudience(Audience);
        string token = proof ? RuntimeWorkerAuthentication.CreateAppProof(key, "app", Audience, now)
            : RuntimeWorkerAuthentication.CreateToken(key, audience, RuntimeWorkerAuthentication.WorkerPurpose, now);
        JsonWebTokenHandler handler = new();
        TokenValidationResult result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = false,
            ValidAudience = audience,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidTypes = ["JWT"],
            ClockSkew = RuntimeWorkerAuthentication.ClockSkew
        });

        Assert.True(result.IsValid);
        JsonWebToken parsed = handler.ReadJsonWebToken(token);
        Assert.Equal(SecurityAlgorithms.HmacSha256, parsed.Alg);
        Assert.Equal(now.ToUnixTimeSeconds(), parsed.IssuedAt.Subtract(DateTime.UnixEpoch).TotalSeconds);
        Assert.Equal(now.AddMinutes(5).ToUnixTimeSeconds(), parsed.ValidTo.Subtract(DateTime.UnixEpoch).TotalSeconds);
        if (proof)
        {
            Assert.Equal(Audience, parsed.GetPayloadValue<string>("worker_id"));
        }
    }

    private static Dictionary<string, object> CreateClaims() => new()
    {
        ["iss"] = RuntimeWorkerAuthentication.ManagementIssuer,
        ["aud"] = Audience,
        ["purpose"] = RuntimeWorkerAuthentication.WorkerPurpose,
        ["iat"] = Now.ToUnixTimeSeconds(),
        ["nbf"] = Now.ToUnixTimeSeconds(),
        ["exp"] = Now.AddMinutes(5).ToUnixTimeSeconds()
    };

    private static string Sign(string header, string payload, byte[]? key = null)
        => SignParts(Base64Url.EncodeToString(Encoding.UTF8.GetBytes(header)),
            Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload)), key);

    private static string SignParts(string header, string payload, byte[]? key = null)
    {
        string content = $"{header}.{payload}";
        string signature = Base64Url.EncodeToString(HMACSHA256.HashData(
            key ?? RuntimeAuthenticationTestData.WorkerKey, Encoding.ASCII.GetBytes(content)));

        return $"{content}.{signature}";
    }
}
