// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;

if (string.Equals(Environment.GetEnvironmentVariable("COMPUTE_SEPARATION_MSI_POC"), "1", StringComparison.Ordinal))
{
    await RequestManagedIdentityTokenAsync();
}

// Direct worker launch has no Core Tools process to supply the directory containing this fixture's user assemblies.
Environment.SetEnvironmentVariable("FUNCTIONS_APPLICATION_DIRECTORY", AppContext.BaseDirectory);

FunctionsApplicationBuilder builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

await builder.Build().RunAsync();

static async Task RequestManagedIdentityTokenAsync()
{
    string endpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT")
        ?? throw new InvalidOperationException("IDENTITY_ENDPOINT is required for the managed identity POC.");
    string identityHeader = Environment.GetEnvironmentVariable("IDENTITY_HEADER")
        ?? throw new InvalidOperationException("IDENTITY_HEADER is required for the managed identity POC.");
    using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(5) };
    using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));

    while (true)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Get,
            $"{endpoint}?resource=https%3A%2F%2Fstorage.azure.com%2F&api-version=2019-08-01");
        request.Headers.TryAddWithoutValidation("X-IDENTITY-HEADER", identityHeader);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, timeout.Token);
        }
        catch (HttpRequestException)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token);
            continue;
        }
        catch (OperationCanceledException) when (!timeout.IsCancellationRequested)
        {
            continue;
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token);
                continue;
            }

            response.EnsureSuccessStatusCode();
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (string.IsNullOrWhiteSpace(body.RootElement.GetProperty("access_token").GetString()))
            {
                throw new InvalidOperationException("The managed identity POC returned no access token.");
            }

            return;
        }
    }
}
