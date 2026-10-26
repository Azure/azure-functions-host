// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string? secret = builder.Configuration["MockTokenService:Secret"];
ArgumentException.ThrowIfNullOrWhiteSpace(secret);
byte[] expectedSecret = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
WebApplication app = builder.Build();
MockTokenServiceState state = new();

app.MapGet("/health", static () => Results.Ok());
app.MapGet("/simulate/state", () => Results.Ok(new
{
    receivedRequests = state.ReceivedRequests,
    authorizedRequests = state.AuthorizedRequests,
    response = state.Response
}));
app.MapPost("/simulate/response", (MockTokenResponse response) =>
{
    if (response.DelayMilliseconds is < 0 or > 120000 || response.StatusCode is < 200 or > 599
        || response.BodyBytes is < 0 or > 2097152 || response.RetryAfterSeconds is < 0 or > 3600)
    {
        return Results.BadRequest("Response controls are outside the supported debugging limits.");
    }

    state.Response = response;
    return Results.Ok();
});
app.MapGet("/token", async (HttpRequest request, CancellationToken cancellationToken) =>
{
    state.RecordRequest();
    if (request.Headers["X-IDENTITY-HEADER"] is not [string supplied]
        || !CryptographicOperations.FixedTimeEquals(expectedSecret, SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
    {
        return Results.Json(new { error = "access_denied", error_description = "The mock service credential is invalid." },
            statusCode: StatusCodes.Status403Forbidden);
    }

    state.RecordAuthorizedRequest();
    MockTokenResponse response = state.Response;
    await Task.Delay(response.DelayMilliseconds, cancellationToken);
    if (response.RetryAfterSeconds > 0)
    {
        request.HttpContext.Response.Headers.RetryAfter = response.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
    }

    if (response.StatusCode is 204 or 205 or 304)
    {
        return Results.StatusCode(response.StatusCode);
    }

    if (response.BodyBytes > 0)
    {
        return Results.Content(new string('x', response.BodyBytes), "text/plain", Encoding.UTF8, statusCode: response.StatusCode);
    }

    if (response.StatusCode != StatusCodes.Status200OK)
    {
        return Results.Json(new { error = "mock_token_service_error", error_description = "Configured debugging response." },
            statusCode: response.StatusCode);
    }

    if (request.Query["resource"] is not [string resource] || string.IsNullOrWhiteSpace(resource))
    {
        return Results.Json(new { error = "invalid_request", error_description = "One resource is required." },
            statusCode: StatusCodes.Status400BadRequest);
    }

    return Results.Json(new
    {
        access_token = "mock-token-not-valid-for-azure",
        expires_on = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
        resource,
        token_type = "Bearer",
        client_id = request.Query["client_id"].FirstOrDefault() ?? "00000000-0000-0000-0000-000000000001"
    });
});

app.Run();

internal partial class Program
{
    internal sealed record MockTokenResponse(int DelayMilliseconds = 0, int StatusCode = 200, int BodyBytes = 0, int RetryAfterSeconds = 0);

    private sealed class MockTokenServiceState
    {
        private long _received;
        private long _authorized;
        private MockTokenResponse _response = new();

        public long ReceivedRequests => Interlocked.Read(ref _received);

        public long AuthorizedRequests => Interlocked.Read(ref _authorized);

        public MockTokenResponse Response
        {
            get => Volatile.Read(ref _response);
            set => Volatile.Write(ref _response, value);
        }

        public void RecordRequest() => Interlocked.Increment(ref _received);

        public void RecordAuthorizedRequest() => Interlocked.Increment(ref _authorized);
    }
}
