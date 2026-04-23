// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.Functions.WorkerProxy.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Microsoft.Azure.Functions.WorkerProxy.Tests;

public class ContainerJwtAuthTests
{
    private const string TestPodName = "test-pod-12345";
    private const string TestContainerName = "test-container-67890";

    // 32 random bytes, base64-encoded — same shape as a real container key.
    private const string TestKeyBase64 = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";

    private static readonly byte[] TestKeyBytes = Convert.FromBase64String(TestKeyBase64);

    // --- Helper ---

    private static Func<string, string?> EnvWith(string? containerKey, string? podName, string? containerName = null, string? siteAuthKey = null)
    {
        return name => name switch
        {
            ContainerJwtAuth.ContainerEncryptionKey => containerKey,
            ContainerJwtAuth.WebSiteAuthEncryptionKey => siteAuthKey,
            ContainerJwtAuth.WebsitePodName => podName,
            ContainerJwtAuth.ContainerName => containerName,
            _ => null
        };
    }

    private static string CreateToken(string audience, string issuer, byte[]? signingKey = null, DateTime? expires = null)
    {
        var key = new SymmetricSecurityKey(signingKey ?? TestKeyBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = expires ?? DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = creds,
            Subject = new ClaimsIdentity([new Claim("sub", "test-subject")])
        };
        return handler.CreateToken(descriptor);
    }

    // --- GetSigningKeys ---

    [Fact]
    public void GetSigningKeys_NoEnvKeys_ReturnsEmpty()
    {
        var keys = ContainerJwtAuth.GetSigningKeys(EnvWith(null, TestPodName));
        Assert.Empty(keys);
    }

    [Fact]
    public void GetSigningKeys_ContainerEncryptionKeyBase64_AddsKey()
    {
        var keys = ContainerJwtAuth.GetSigningKeys(EnvWith(TestKeyBase64, TestPodName));
        var key = Assert.Single(keys);
        Assert.Equal(TestKeyBytes, key.Key);
    }

    [Fact]
    public void GetSigningKeys_ContainerEncryptionKeyHex_AddsKey()
    {
        // 64-char hex form — alternative encoding accepted by the runtime.
        string hex = Convert.ToHexString(TestKeyBytes);
        Assert.Equal(64, hex.Length);
        var keys = ContainerJwtAuth.GetSigningKeys(EnvWith(hex, TestPodName));
        var key = Assert.Single(keys);
        Assert.Equal(TestKeyBytes, key.Key);
    }

    [Fact]
    public void GetSigningKeys_BothKeys_AddsBoth()
    {
        // Two different valid keys → both registered as candidate signing keys.
        var second = Convert.ToBase64String(new byte[32]);
        var keys = ContainerJwtAuth.GetSigningKeys(EnvWith(TestKeyBase64, TestPodName, siteAuthKey: second));
        Assert.Equal(2, keys.Length);
    }

    [Fact]
    public void GetSigningKeys_MalformedKey_Throws()
    {
        // Not valid base64 and not 64-char hex → FormatException bubbles out
        // of the parser. Mirrors runtime: SecretsUtility.GetTokenIssuerSigningKeys
        // does not catch, so a misconfigured CONTAINER_ENCRYPTION_KEY fails
        // startup loudly instead of silently rejecting every admin call.
        Assert.Throws<FormatException>(() => ContainerJwtAuth.GetSigningKeys(EnvWith("not-a-real-key", TestPodName)));
    }

    // --- GetValidAudiences ---

    [Fact]
    public void GetValidAudiences_PodName_Included()
    {
        var audiences = ContainerJwtAuth.GetValidAudiences(EnvWith(TestKeyBase64, TestPodName));
        Assert.Contains(TestPodName, audiences);
    }

    [Fact]
    public void GetValidAudiences_ContainerName_Included()
    {
        var audiences = ContainerJwtAuth.GetValidAudiences(EnvWith(TestKeyBase64, podName: null, containerName: TestContainerName));
        Assert.Contains(TestContainerName, audiences);
    }

    [Fact]
    public void GetValidAudiences_NeitherSet_Empty()
    {
        var audiences = ContainerJwtAuth.GetValidAudiences(EnvWith(TestKeyBase64, podName: null, containerName: null));
        Assert.Empty(audiences);
    }

    // --- CreateTokenValidationParameters ---

    [Fact]
    public void CreateValidationParameters_NoKey_LeavesIssuerSigningKeysNull()
    {
        // Mirrors the runtime: with no key, the validation parameters carry no
        // signing keys, so every token presented at runtime fails validation.
        var p = ContainerJwtAuth.CreateTokenValidationParameters(EnvWith(null, TestPodName));
        Assert.Null(p.IssuerSigningKeys);
    }

    [Fact]
    public void CreateValidationParameters_WithKey_PopulatesAllFields()
    {
        var p = ContainerJwtAuth.CreateTokenValidationParameters(EnvWith(TestKeyBase64, TestPodName));
        Assert.NotNull(p.IssuerSigningKeys);
        Assert.Single(p.IssuerSigningKeys);
        Assert.Contains(ContainerJwtAuth.AppServiceCoreUri, p.ValidIssuers);
        Assert.Contains(ContainerJwtAuth.LegionCoreUri, p.ValidIssuers);
        Assert.Contains(TestPodName, p.ValidAudiences);
    }

    // --- End-to-end through the JwtBearer middleware ---

    private static async Task<TestServer> CreateServerAsync(Func<string, string?> getEnv)
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(s =>
            {
                s.AddRouting();
                s.AddContainerJwtAuth(getEnv);
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(e =>
                {
                    var admin = e.MapGroup("/admin");
                    admin.MapGet("/worker/ready", () => Results.Ok()).AllowAnonymous();

                    var adminAuthed = admin.MapGroup(string.Empty).RequireAuthorization();
                    adminAuthed.MapPost("/worker/assign", () => Results.Ok());
                    adminAuthed.MapPost("/worker/drain", () => Results.Ok());
                    adminAuthed.MapPost("/infra/instanceState", () => Results.Ok());
                });
            });
        });

        var host = await builder.StartAsync();
        return host.GetTestServer();
    }

    private static HttpRequestMessage Get(string path, string? bearer = null, string? siteToken = null)
        => BuildRequest(HttpMethod.Get, path, bearer, siteToken);

    private static HttpRequestMessage Post(string path, string? bearer = null, string? siteToken = null)
        => BuildRequest(HttpMethod.Post, path, bearer, siteToken);

    private static HttpRequestMessage BuildRequest(HttpMethod method, string path, string? bearer = null, string? siteToken = null)
    {
        var req = new HttpRequestMessage(method, path);
        if (bearer is not null)
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (siteToken is not null)
        {
            req.Headers.Add(ContainerJwtAuth.SiteTokenHeaderName, siteToken);
        }

        return req;
    }

    [Fact]
    public async Task ReadyEndpoint_Anonymous_Returns200()
    {
        // /admin/worker/ready is anonymous: NNA polls it before specialization,
        // before any encryption key has been delivered, so it cannot present a
        // container-issued JWT.
        using var server = await CreateServerAsync(EnvWith(TestKeyBase64, TestPodName));
        var resp = await server.CreateClient().SendAsync(Get("/admin/worker/ready"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task GuardedEndpoint_NoToken_Returns401()
    {
        using var server = await CreateServerAsync(EnvWith(TestKeyBase64, TestPodName));
        var resp = await server.CreateClient().SendAsync(Post("/admin/worker/assign"));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Endpoint_ValidBearerToken_Returns200()
    {
        using var server = await CreateServerAsync(EnvWith(TestKeyBase64, TestPodName));
        var token = CreateToken(audience: TestPodName, issuer: ContainerJwtAuth.LegionCoreUri);
        var resp = await server.CreateClient().SendAsync(Post("/admin/worker/assign", bearer: token));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Endpoint_ValidSiteTokenHeader_Returns200()
    {
        using var server = await CreateServerAsync(EnvWith(TestKeyBase64, TestPodName));
        var token = CreateToken(audience: TestPodName, issuer: ContainerJwtAuth.LegionCoreUri);
        var resp = await server.CreateClient().SendAsync(Post("/admin/worker/assign", siteToken: token));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Endpoint_AppServiceCoreIssuer_Returns200()
    {
        using var server = await CreateServerAsync(EnvWith(TestKeyBase64, TestPodName));
        var token = CreateToken(audience: TestPodName, issuer: ContainerJwtAuth.AppServiceCoreUri);
        var resp = await server.CreateClient().SendAsync(Post("/admin/worker/assign", bearer: token));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Endpoint_AudienceFromContainerName_Returns200()
    {
        using var server = await CreateServerAsync(EnvWith(TestKeyBase64, podName: null, containerName: TestContainerName));
        var token = CreateToken(audience: TestContainerName, issuer: ContainerJwtAuth.LegionCoreUri);
        var resp = await server.CreateClient().SendAsync(Post("/admin/worker/assign", bearer: token));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Endpoint_WrongSigningKey_Returns401()
    {
        using var server = await CreateServerAsync(EnvWith(TestKeyBase64, TestPodName));
        var wrongKey = new byte[32];
        wrongKey[0] = 0xFF;
        var token = CreateToken(audience: TestPodName, issuer: ContainerJwtAuth.LegionCoreUri, signingKey: wrongKey);
        var resp = await server.CreateClient().SendAsync(Post("/admin/worker/assign", bearer: token));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Endpoint_WrongIssuer_Returns401()
    {
        using var server = await CreateServerAsync(EnvWith(TestKeyBase64, TestPodName));
        var token = CreateToken(audience: TestPodName, issuer: "https://attacker.example.com");
        var resp = await server.CreateClient().SendAsync(Post("/admin/worker/assign", bearer: token));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Endpoint_WrongAudience_Returns401()
    {
        using var server = await CreateServerAsync(EnvWith(TestKeyBase64, TestPodName));
        var token = CreateToken(audience: "some-other-pod", issuer: ContainerJwtAuth.LegionCoreUri);
        var resp = await server.CreateClient().SendAsync(Post("/admin/worker/assign", bearer: token));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Endpoint_ExpiredToken_Returns401()
    {
        using var server = await CreateServerAsync(EnvWith(TestKeyBase64, TestPodName));
        var token = CreateToken(audience: TestPodName, issuer: ContainerJwtAuth.LegionCoreUri, expires: DateTime.UtcNow.AddMinutes(-10));
        var resp = await server.CreateClient().SendAsync(Post("/admin/worker/assign", bearer: token));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Endpoint_NoEncryptionKeyConfigured_AllTokensRejected()
    {
        // Fail-closed parity with the runtime: when no key is configured,
        // even a token signed with what we *think* is the right key is
        // rejected because the validation parameters carry no signing keys.
        using var server = await CreateServerAsync(EnvWith(null, TestPodName));
        var token = CreateToken(audience: TestPodName, issuer: ContainerJwtAuth.LegionCoreUri);
        var resp = await server.CreateClient().SendAsync(Post("/admin/worker/assign", bearer: token));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Theory]
    [InlineData("/admin/worker/assign", "POST")]
    [InlineData("/admin/worker/drain", "POST")]
    [InlineData("/admin/infra/instanceState", "POST")]
    public async Task GuardedAdminEndpoints_RequireAuth(string path, string method)
    {
        // Regression: every guarded admin endpoint inherits auth via the nested
        // MapGroup. Asserts that an unauthenticated request to each one returns
        // 401, not 200. /admin/worker/ready is intentionally NOT in this list —
        // it is anonymous (covered by ReadyEndpoint_Anonymous_Returns200).
        using var server = await CreateServerAsync(EnvWith(TestKeyBase64, TestPodName));
        var req = new HttpRequestMessage(new HttpMethod(method), path);
        var resp = await server.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task SiteTokenHeader_TakesPrecedenceOverAuthorizationHeader()
    {
        // Matches runtime behavior: when both headers are present, x-ms-site-token wins.
        using var server = await CreateServerAsync(EnvWith(TestKeyBase64, TestPodName));
        var validToken = CreateToken(audience: TestPodName, issuer: ContainerJwtAuth.LegionCoreUri);
        var bogusBearer = "this.is.not-a-valid-jwt";

        var resp = await server.CreateClient().SendAsync(Post("/admin/worker/assign", bearer: bogusBearer, siteToken: validToken));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // --- Parity with runtime: case-insensitive issuer / audience ---

    [Fact]
    public async Task Endpoint_IssuerCasingDiffers_IsAccepted()
    {
        using var server = await CreateServerAsync(EnvWith(TestKeyBase64, TestPodName));
        var token = CreateToken(TestPodName, "https://Legion.Core.AzureWebsites.NET");
        var resp = await server.CreateClient().SendAsync(Post("/admin/worker/assign", bearer: token));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Endpoint_AudienceCasingDiffers_IsAccepted()
    {
        using var server = await CreateServerAsync(EnvWith(TestKeyBase64, TestPodName));
        var token = CreateToken(TestPodName.ToUpperInvariant(), ContainerJwtAuth.LegionCoreUri);
        var resp = await server.CreateClient().SendAsync(Post("/admin/worker/assign", bearer: token));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public void KeyParser_ToKeyBytes_MalformedHex_Throws()
    {
        // 64 chars but not valid hex (contains 'Z'). Both the runtime and the
        // proxy let the FormatException bubble so a misconfigured encryption
        // key fails loudly at startup rather than silently dropping the key
        // and 401-ing every admin request with no diagnostic.
        const string MalformedHex = "ZZ75CA46E7EBDD39E4CA6B074D1F9A5972B849A55F91A248F6B038A61BACE9D7";
        Assert.Throws<FormatException>(() => SiteTokenKeyParser.ToKeyBytes(MalformedHex));
    }
}
