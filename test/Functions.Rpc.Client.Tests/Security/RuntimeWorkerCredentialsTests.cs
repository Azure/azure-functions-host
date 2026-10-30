// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Cryptography;
using Azure.Functions.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
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

    [Fact]
    public void AppAudience_CanonicalizesTrustedAppNamesWithoutRelaxingSignedClaimsOrWorkerIdentity()
    {
        byte[] key = Convert.FromHexString(RuntimeWorkerTestData.AppKey);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string proof = RuntimeWorkerAuthentication.CreateAppProof(key, "test-app", "Worker-1", now);

        Assert.True(RuntimeWorkerAuthentication.ValidateAppProof(proof, key, "TeSt-App", "Worker-1", now));
        Assert.False(RuntimeWorkerAuthentication.ValidateAppProof(proof, key, "different-app", "Worker-1", now));
        Assert.False(RuntimeWorkerAuthentication.ValidateAppProof(proof, key, "test-app", "worker-1", now));
        string noncanonicalClaim = RuntimeWorkerAuthentication.CreateToken(key, "azure-functions-app:TEST-APP",
            RuntimeWorkerAuthentication.AppPurpose, now, "Worker-1");
        Assert.False(RuntimeWorkerAuthentication.ValidateAppProof(noncanonicalClaim, key, "TEST-APP", "Worker-1", now));
        string wrongPurpose = RuntimeWorkerAuthentication.CreateToken(key, RuntimeWorkerAuthentication.AppAudience("TEST-APP"),
            RuntimeWorkerAuthentication.WorkerPurpose, now, "Worker-1");
        Assert.False(RuntimeWorkerAuthentication.ValidateAppProof(wrongPurpose, key, "test-app", "Worker-1", now));
    }

    [Fact]
    public void ManagementToken_UsesPlatformIssuerPodAudienceAndLifetimeWithoutDataProofClaims()
    {
        byte[] key = Convert.FromBase64String(RuntimeWorkerTestData.WorkerKey);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        JwtSecurityTokenHandler handler = new();
        JwtSecurityToken jwt = new(
            issuer: "https://legion.core.azurewebsites.net",
            audience: "runtime-pod",
            notBefore: now.AddMinutes(-1).UtcDateTime,
            expires: now.AddMinutes(10).UtcDateTime,
            signingCredentials: new(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256));
        string token = handler.WriteToken(jwt);

        Assert.False(jwt.Payload.ContainsKey("iat"));
        Assert.False(jwt.Payload.ContainsKey("purpose"));
        Assert.Equal(TokenValidationParameters.DefaultClockSkew, RuntimeWorkerAuthentication.ManagementClockSkew);
        Assert.True(RuntimeWorkerAuthentication.ValidateManagementToken(token, key, "runtime-pod", now));
        Assert.False(RuntimeWorkerAuthentication.ValidateManagementToken(token, key, "worker-pod", now));
        Assert.False(RuntimeWorkerAuthentication.ValidateAppProof(token, key, "runtime-pod", "worker-pod", now));
        Assert.False(RuntimeWorkerAuthentication.ValidateToken(token, key,
            RuntimeWorkerAuthentication.WorkerAudience("runtime-pod"), RuntimeWorkerAuthentication.WorkerPurpose, now));
    }

    [Theory]
    [InlineData("missing-key", "MissingKey")]
    [InlineData("invalid-key", "InvalidKey")]
    [InlineData("missing-proof", "MissingToken")]
    [InlineData("wrong-key", "Signature")]
    [InlineData("wrong-app", "Audience")]
    [InlineData("wrong-worker", "WorkerIdentity")]
    [InlineData("wrong-purpose", "Purpose")]
    [InlineData("expired", "Lifetime")]
    public void ReverseProofFailure_LogsSafeCategoryWithoutProofKeysOrClaims(string scenario, string reason)
    {
        IConfiguration configuration = RuntimeWorkerTestData.CreateConfiguration();
        if (string.Equals(scenario, "missing-key", StringComparison.Ordinal))
        {
            configuration["WEBSITE_AUTH_ENCRYPTION_KEY"] = null;
        }
        if (string.Equals(scenario, "invalid-key", StringComparison.Ordinal))
        {
            configuration["WEBSITE_AUTH_ENCRYPTION_KEY"] = "private-invalid-key";
        }
        DateTimeOffset now = DateTimeOffset.UtcNow;
        byte[] key = Convert.FromHexString(RuntimeWorkerTestData.AppKey);
        string proof = scenario switch
        {
            "missing-proof" => null,
            "wrong-key" => RuntimeWorkerAuthentication.CreateAppProof(Convert.FromBase64String(RuntimeWorkerTestData.WorkerKey),
                "test-app", "worker", now),
            "wrong-app" => RuntimeWorkerAuthentication.CreateAppProof(key, "private-customer-app", "worker", now),
            "wrong-worker" => RuntimeWorkerAuthentication.CreateAppProof(key, "test-app", "private-customer-worker", now),
            "wrong-purpose" => RuntimeWorkerAuthentication.CreateToken(key, RuntimeWorkerAuthentication.AppAudience("test-app"),
                "private-customer-purpose", now, "worker"),
            "expired" => RuntimeWorkerAuthentication.CreateAppProof(key, "test-app", "worker", now.AddMinutes(-6)),
            _ => RuntimeWorkerTestData.CreateAppProof("worker")
        };
        string logged = string.Empty;
        Mock<ILogger> logger = new();
        logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        logger.Setup(value => value.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()))
            .Callback(new InvocationAction(call =>
                logged = (string)((Delegate)call.Arguments[4]).DynamicInvoke(call.Arguments[2], call.Arguments[3])));
        RuntimeWorkerCredentials credentials = new("worker", RuntimeWorkerTestData.WorkerKey, configuration, TimeProvider.System, logger.Object);

        Assert.Throws<WorkerLinkException>(() => credentials.ValidateAppProof(proof));

        Assert.Contains(reason, logged, StringComparison.Ordinal);
        Assert.DoesNotContain(RuntimeWorkerTestData.WorkerKey, logged, StringComparison.Ordinal);
        Assert.DoesNotContain(RuntimeWorkerTestData.AppKey, logged, StringComparison.Ordinal);
        Assert.DoesNotContain("private-", logged, StringComparison.Ordinal);
        if (proof is not null)
        {
            Assert.DoesNotContain(proof, logged, StringComparison.Ordinal);
        }
    }
}
