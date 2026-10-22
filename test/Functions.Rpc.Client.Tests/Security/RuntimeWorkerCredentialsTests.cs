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
    public void SharedContract_UsesOneCallerHeaderAndASeparateWorkerBoundAppProof()
    {
        RuntimeWorkerCredentials credentials = RuntimeWorkerTestData.CreateCredentials("worker");
        IReadOnlyDictionary<string, string> headers = credentials.CreateHeaders();
        Assert.Equal(["x-ms-functions-worker-token"], headers.Keys);
        byte[] workerKey = Convert.FromBase64String(RuntimeWorkerTestData.WorkerKey);
        Assert.True(RuntimeWorkerAuthentication.ValidateHeaders(name => headers[name], workerKey, "worker", DateTimeOffset.UtcNow));
        Assert.False(RuntimeWorkerAuthentication.ValidateHeaders(_ => null, workerKey, "worker", DateTimeOffset.UtcNow));
        string proof = RuntimeWorkerTestData.CreateAppProof("worker");
        Dictionary<string, string> capabilities = new();
        RuntimeWorkerAuthentication.SetAppProof(capabilities, proof);
        Assert.Equal(["WorkerProxyAppToken"], capabilities.Keys);
        Assert.Equal(proof, RuntimeWorkerAuthentication.RemoveAppProof(capabilities));
        Assert.Empty(capabilities);
        credentials.ValidateAppProof(proof);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothDirections_InteroperateWithStandardJwtValidation(bool appProof)
    {
        RuntimeWorkerCredentials credentials = RuntimeWorkerTestData.CreateCredentials("worker");
        string encoded = appProof ? RuntimeWorkerTestData.CreateAppProof("worker")
            : credentials.CreateHeaders()[RuntimeWorkerAuthentication.WorkerTokenHeader];
        byte[] key = appProof ? Convert.FromHexString(RuntimeWorkerTestData.AppKey) : Convert.FromBase64String(RuntimeWorkerTestData.WorkerKey);
        string audience = appProof ? RuntimeWorkerAuthentication.AppAudience("test-app") : RuntimeWorkerAuthentication.WorkerAudience("worker");
        JwtSecurityTokenHandler handler = new();
        handler.ValidateToken(encoded, new TokenValidationParameters
        {
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = false,
            ValidAudience = audience,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = RuntimeWorkerAuthentication.ClockSkew,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        }, out SecurityToken token);
        JwtSecurityToken jwt = Assert.IsType<JwtSecurityToken>(token);
        Assert.Equal(audience, Assert.Single(jwt.Audiences));
        if (appProof)
        {
            Assert.Equal("worker", jwt.Payload["worker_id"]);
        }
    }

    [Theory]
    [InlineData("WEBSITE_SITE_NAME", null)]
    [InlineData("WEBSITE_SITE_NAME", " ")]
    [InlineData("WEBSITE_AUTH_ENCRYPTION_KEY", null)]
    [InlineData("WEBSITE_AUTH_ENCRYPTION_KEY", "invalid-key")]
    public void MissingAppConfiguration_DoesNotBlockCallerSigningButRejectsReverseProof(string setting, string value)
    {
        IConfiguration configuration = RuntimeWorkerTestData.CreateConfiguration();
        configuration[setting] = value;
        configuration["CONTAINER_ENCRYPTION_KEY"] = RuntimeWorkerTestData.AppKey;
        RuntimeWorkerCredentials credentials = new("worker", RuntimeWorkerTestData.WorkerKey, configuration, TimeProvider.System);

        Assert.Single(credentials.CreateHeaders());
        WorkerLinkException error = Assert.Throws<WorkerLinkException>(() => credentials.ValidateAppProof(RuntimeWorkerTestData.CreateAppProof("worker")));
        Assert.Equal(WorkerLinkFailureReason.AuthenticationUnavailable, error.Reason);
        Assert.DoesNotContain(RuntimeWorkerTestData.AppKey, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContainerKey_UsesPlatformHexOrBase64Convention(bool hex)
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        string encoded = hex ? Convert.ToHexString(key) : Convert.ToBase64String(key);
        RuntimeWorkerCredentials credentials = new("worker", encoded, new ConfigurationBuilder().Build(), TimeProvider.System);
        string token = credentials.CreateHeaders()[RuntimeWorkerAuthentication.WorkerTokenHeader];

        Assert.True(RuntimeWorkerAuthentication.ValidateToken(token, key,
            RuntimeWorkerAuthentication.WorkerAudience("worker"), RuntimeWorkerAuthentication.WorkerPurpose, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CallerSigning_UsesLinkedProxyKeyInsteadOfRuntimeOwnContainerOrAppKey()
    {
        IConfiguration configuration = RuntimeWorkerTestData.CreateConfiguration();
        byte[] runtimeKey = SHA256.HashData("runtime-container-key-is-not-worker-key"u8);
        configuration["CONTAINER_ENCRYPTION_KEY"] = Convert.ToHexString(runtimeKey);
        RuntimeWorkerCredentials credentials = new("worker", RuntimeWorkerTestData.WorkerKey, configuration, TimeProvider.System);
        string token = credentials.CreateHeaders()[RuntimeWorkerAuthentication.WorkerTokenHeader];
        string audience = RuntimeWorkerAuthentication.WorkerAudience("worker");
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Assert.True(RuntimeWorkerAuthentication.ValidateToken(token, Convert.FromBase64String(RuntimeWorkerTestData.WorkerKey),
            audience, RuntimeWorkerAuthentication.WorkerPurpose, now));
        Assert.False(RuntimeWorkerAuthentication.ValidateToken(token, runtimeKey, audience, RuntimeWorkerAuthentication.WorkerPurpose, now));
        Assert.False(RuntimeWorkerAuthentication.ValidateToken(token, Convert.FromHexString(RuntimeWorkerTestData.AppKey),
            audience, RuntimeWorkerAuthentication.WorkerPurpose, now));
    }
}
