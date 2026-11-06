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
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public class RuntimeWorkerCredentialsTests
{
    [Fact]
    public void CallerHeaders_AreSignedOnceAndCanBeEnumeratedAgainWithoutResigning()
    {
        Mock<TimeProvider> time = new(MockBehavior.Strict);
        time.Setup(value => value.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
        RuntimeWorkerCredentials credentials = new("worker", RuntimeWorkerTestData.WorkerKey, time.Object);

        IEnumerable<KeyValuePair<string, string>> headers = credentials.CreateHeaders();

        time.Verify(value => value.GetUtcNow(), Times.Once);
        Assert.False(headers is IReadOnlyDictionary<string, string>);
        Assert.Equal(Assert.Single(headers).Value, Assert.Single(headers).Value);
        time.Verify(value => value.GetUtcNow(), Times.Once);
        time.VerifyNoOtherCalls();
    }

    [Fact]
    public void CallerSigning_DoesNotReadApplicationConfiguration()
    {
        Mock<IConfiguration> configuration = new(MockBehavior.Strict);
        _ = RuntimeWorkerTestData.CreateApplicationOptions(configuration.Object);
        RuntimeWorkerCredentials credentials = new("worker", RuntimeWorkerTestData.WorkerKey, TimeProvider.System);

        IEnumerable<KeyValuePair<string, string>> headers = credentials.CreateHeaders();

        Assert.True(RuntimeWorkerAuthentication.ValidateToken(
            Assert.Single(headers).Value,
            Convert.FromBase64String(RuntimeWorkerTestData.WorkerKey),
            RuntimeWorkerAuthentication.WorkerAudience("worker"),
            RuntimeWorkerAuthentication.WorkerPurpose,
            DateTimeOffset.UtcNow));
        configuration.VerifyNoOtherCalls();
    }

    [Fact]
    public void AppProof_ReadsCurrentApplicationKeyOncePerValidationWithoutReload()
    {
        string currentKey = RuntimeWorkerTestData.AppKey;
        Mock<IConfiguration> configuration = new(MockBehavior.Strict);
        configuration.SetupGet(value => value[RuntimeWorkerAuthentication.AppNameSetting]).Returns("test-app");
        configuration.SetupGet(value => value[RuntimeWorkerAuthentication.AppKeySetting]).Returns(() => currentKey);
        RuntimeWorkerCredentials credentials = new("worker", RuntimeWorkerTestData.WorkerKey, TimeProvider.System);
        IOptionsFactory<RuntimeApplicationAuthenticationOptions> options = RuntimeWorkerTestData.CreateApplicationOptions(configuration.Object);
        string originalProof = RuntimeWorkerTestData.CreateAppProof("worker");

        ValidateProof(originalProof);
        currentKey = Convert.ToHexString(SHA256.HashData("replacement-app-key"u8));
        WorkerLinkException rejected = Assert.Throws<WorkerLinkException>(() => ValidateProof(originalProof));
        Assert.Equal(WorkerLinkFailureReason.Unavailable, rejected.Reason);
        ValidateProof(RuntimeWorkerAuthentication.CreateAppProof(
            Convert.FromHexString(currentKey), "test-app", "worker", DateTimeOffset.UtcNow));

        configuration.VerifyGet(value => value[RuntimeWorkerAuthentication.AppNameSetting], Times.Exactly(3));
        configuration.VerifyGet(value => value[RuntimeWorkerAuthentication.AppKeySetting], Times.Exactly(3));
        configuration.VerifyNoOtherCalls();

        void ValidateProof(string proof)
        {
            RuntimeApplicationAuthenticationOptions snapshot = options.Create(Options.DefaultName);
            credentials.ValidateAppProof(proof, snapshot.AppName, snapshot.AppKey);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-app-key")]
    public void AppProof_InvalidApplicationKeyIsReadOnlyOnce(string key)
    {
        Mock<IConfiguration> configuration = new(MockBehavior.Strict);
        configuration.SetupGet(value => value[RuntimeWorkerAuthentication.AppNameSetting]).Returns("test-app");
        configuration.SetupGet(value => value[RuntimeWorkerAuthentication.AppKeySetting]).Returns(key);
        IOptionsFactory<RuntimeApplicationAuthenticationOptions> options = RuntimeWorkerTestData.CreateApplicationOptions(configuration.Object);

        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => options.Create(Options.DefaultName));

        Assert.Equal([key is null ? "MissingKey" : "InvalidKey"], error.Failures);
        configuration.VerifyGet(value => value[RuntimeWorkerAuthentication.AppNameSetting], Times.Once);
        configuration.VerifyGet(value => value[RuntimeWorkerAuthentication.AppKeySetting], Times.Once);
        configuration.VerifyNoOtherCalls();
    }

    [Fact]
    public void MatchesKey_ComparesDecodedCredentialBytesAcrossEncodings()
    {
        byte[] key = Convert.FromBase64String(RuntimeWorkerTestData.WorkerKey);
        RuntimeWorkerCredentials base64 = new("worker", Convert.ToBase64String(key), TimeProvider.System);
        RuntimeWorkerCredentials hex = new("worker", Convert.ToHexString(key), TimeProvider.System);
        key[0] ^= 1;
        RuntimeWorkerCredentials different = new("worker", Convert.ToBase64String(key), TimeProvider.System);

        Assert.True(base64.MatchesKey(hex));
        Assert.True(hex.MatchesKey(base64));
        Assert.False(base64.MatchesKey(different));
        Assert.False(different.MatchesKey(base64));
    }

    [Fact]
    public void SharedContract_UsesOneCallerHeaderAndASeparateWorkerBoundAppProof()
    {
        RuntimeWorkerCredentials credentials = RuntimeWorkerTestData.CreateCredentials("worker");
        IEnumerable<KeyValuePair<string, string>> headers = credentials.CreateHeaders();
        KeyValuePair<string, string> header = Assert.Single(headers);
        Assert.Equal("x-ms-functions-worker-token", header.Key);
        byte[] workerKey = Convert.FromBase64String(RuntimeWorkerTestData.WorkerKey);
        Assert.True(RuntimeWorkerAuthentication.ValidateToken(header.Value, workerKey,
            RuntimeWorkerAuthentication.WorkerAudience("worker"), RuntimeWorkerAuthentication.WorkerPurpose, DateTimeOffset.UtcNow));
        Assert.False(RuntimeWorkerAuthentication.ValidateToken(null, workerKey,
            RuntimeWorkerAuthentication.WorkerAudience("worker"), RuntimeWorkerAuthentication.WorkerPurpose, DateTimeOffset.UtcNow));
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
            : Assert.Single(credentials.CreateHeaders()).Value;
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
        RuntimeWorkerCredentials credentials = new("worker", RuntimeWorkerTestData.WorkerKey, TimeProvider.System);
        IOptionsFactory<RuntimeApplicationAuthenticationOptions> options = RuntimeWorkerTestData.CreateApplicationOptions(configuration);

        Assert.Single(credentials.CreateHeaders());
        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => options.Create(Options.DefaultName));
        Assert.Equal([string.Equals(setting, RuntimeWorkerAuthentication.AppNameSetting, StringComparison.Ordinal)
            ? "MissingIdentity" : value is null ? "MissingKey" : "InvalidKey"], error.Failures);
        Assert.DoesNotContain(RuntimeWorkerTestData.AppKey, error.Message, StringComparison.Ordinal);

        configuration[setting] = string.Equals(setting, RuntimeWorkerAuthentication.AppNameSetting, StringComparison.Ordinal)
            ? "test-app" : RuntimeWorkerTestData.AppKey;
        RuntimeApplicationAuthenticationOptions snapshot = options.Create(Options.DefaultName);
        credentials.ValidateAppProof(RuntimeWorkerTestData.CreateAppProof("worker"), snapshot.AppName, snapshot.AppKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContainerKey_UsesPlatformHexOrBase64Convention(bool hex)
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        string encoded = hex ? Convert.ToHexString(key) : Convert.ToBase64String(key);
        RuntimeWorkerCredentials credentials = new("worker", encoded, TimeProvider.System);
        string token = Assert.Single(credentials.CreateHeaders()).Value;

        Assert.True(RuntimeWorkerAuthentication.ValidateToken(token, key,
            RuntimeWorkerAuthentication.WorkerAudience("worker"), RuntimeWorkerAuthentication.WorkerPurpose, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CallerSigning_UsesLinkedProxyKeyInsteadOfRuntimeOwnContainerOrAppKey()
    {
        IConfiguration configuration = RuntimeWorkerTestData.CreateConfiguration();
        byte[] runtimeKey = SHA256.HashData("runtime-container-key-is-not-worker-key"u8);
        configuration["CONTAINER_ENCRYPTION_KEY"] = Convert.ToHexString(runtimeKey);
        RuntimeWorkerCredentials credentials = new("worker", RuntimeWorkerTestData.WorkerKey, TimeProvider.System);
        string token = Assert.Single(credentials.CreateHeaders()).Value;
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
    public void ReverseProofFailure_ReportsSafeCategoryWithoutProofKeysOrClaims(string scenario, string reason)
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
        RuntimeWorkerCredentials credentials = new("worker", RuntimeWorkerTestData.WorkerKey, TimeProvider.System, logger.Object);
        IOptionsFactory<RuntimeApplicationAuthenticationOptions> options = RuntimeWorkerTestData.CreateApplicationOptions(configuration);

        if (string.Equals(scenario, "missing-key", StringComparison.Ordinal)
            || string.Equals(scenario, "invalid-key", StringComparison.Ordinal))
        {
            logged = Assert.Throws<OptionsValidationException>(() => options.Create(Options.DefaultName)).Message;
        }
        else
        {
            RuntimeApplicationAuthenticationOptions snapshot = options.Create(Options.DefaultName);
            Assert.Throws<WorkerLinkException>(() => credentials.ValidateAppProof(proof, snapshot.AppName, snapshot.AppKey));
        }

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
