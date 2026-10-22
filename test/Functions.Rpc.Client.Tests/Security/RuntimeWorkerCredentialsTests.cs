// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Cryptography;
using Azure.Functions.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public class RuntimeWorkerCredentialsTests
{
    [Fact]
    public void SharedContract_DefinesCompleteHeaderLayoutAndValidatesBothProofs()
    {
        RuntimeWorkerCredentials credentials = RuntimeWorkerTestData.CreateCredentials("worker");
        IReadOnlyDictionary<string, string> headers = credentials.CreateHeaders();
        byte[] workerKey = Convert.FromBase64String(RuntimeWorkerTestData.WorkerKey);
        byte[] appKey = Convert.FromHexString(RuntimeWorkerTestData.AppKey);

        Assert.Equal(["x-ms-functions-app-token", "x-ms-functions-worker-token"], headers.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(headers.Keys.Order(StringComparer.Ordinal), RuntimeWorkerAuthentication.TokenHeaders.Order(StringComparer.Ordinal));
        Assert.True(RuntimeWorkerAuthentication.ValidateHeaders(name => headers[name],
            workerKey, "worker", appKey, "test-app", DateTimeOffset.UtcNow));
        Assert.False(RuntimeWorkerAuthentication.ValidateHeaders(
            name => string.Equals(name, "x-ms-functions-app-token", StringComparison.Ordinal) ? null : headers[name],
            workerKey, "worker", appKey, "test-app", DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateHeaders_InteroperatesWithStandardJwtValidation(bool workerToken)
    {
        RuntimeWorkerCredentials credentials = RuntimeWorkerTestData.CreateCredentials("expected-worker");
        IReadOnlyDictionary<string, string> headers = credentials.CreateHeaders();
        byte[] key = workerToken ? Convert.FromBase64String(RuntimeWorkerTestData.WorkerKey)
            : Convert.FromHexString(RuntimeWorkerTestData.AppKey);
        string audience = workerToken ? RuntimeWorkerAuthentication.WorkerAudience("expected-worker")
            : RuntimeWorkerAuthentication.AppAudience("test-app");
        JwtSecurityTokenHandler handler = new();
        TokenValidationParameters parameters = new()
        {
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = false,
            ValidateAudience = true,
            ValidAudience = audience,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = RuntimeWorkerAuthentication.ClockSkew,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };

        handler.ValidateToken(headers[workerToken ? RuntimeWorkerAuthentication.WorkerTokenHeader : RuntimeWorkerAuthentication.AppTokenHeader],
            parameters, out SecurityToken validated);

        Assert.Equal(audience, Assert.Single(Assert.IsType<JwtSecurityToken>(validated).Audiences));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidateToken_AcceptsStandardHs256Jwt(bool workerToken)
    {
        byte[] key = SHA256.HashData("standard-jwt-test-key"u8);
        string audience = workerToken ? RuntimeWorkerAuthentication.WorkerAudience("worker")
            : RuntimeWorkerAuthentication.AppAudience("app");
        string purpose = workerToken ? RuntimeWorkerAuthentication.WorkerPurpose : RuntimeWorkerAuthentication.AppPurpose;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        JwtPayload payload = new()
        {
            ["aud"] = audience,
            ["purpose"] = purpose,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(5).ToUnixTimeSeconds()
        };
        JwtSecurityToken token = new(new JwtHeader(new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)), payload);
        string encoded = new JwtSecurityTokenHandler().WriteToken(token);

        Assert.True(RuntimeWorkerAuthentication.ValidateToken(encoded, key, audience, purpose, now));
    }

    [Theory]
    [InlineData("WEBSITE_SITE_NAME", null)]
    [InlineData("WEBSITE_SITE_NAME", " ")]
    [InlineData("WEBSITE_AUTH_ENCRYPTION_KEY", null)]
    [InlineData("WEBSITE_AUTH_ENCRYPTION_KEY", "invalid-key")]
    public void CreateHeaders_MissingAppConfigurationFailsWithoutManagementKeyFallback(string setting, string value)
    {
        IConfiguration configuration = RuntimeWorkerTestData.CreateConfiguration();
        configuration[setting] = value;
        configuration["CONTAINER_ENCRYPTION_KEY"] = RuntimeWorkerTestData.AppKey;
        RuntimeWorkerCredentials credentials = new("worker", RuntimeWorkerTestData.WorkerKey, configuration, TimeProvider.System);

        WorkerLinkException failure = Assert.Throws<WorkerLinkException>(() => credentials.CreateHeaders());

        Assert.Equal(WorkerLinkFailureReason.AuthenticationUnavailable, failure.Reason);
        Assert.DoesNotContain(RuntimeWorkerTestData.AppKey, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RuntimeWorkerTestData.WorkerKey, failure.Message, StringComparison.Ordinal);
    }
}
