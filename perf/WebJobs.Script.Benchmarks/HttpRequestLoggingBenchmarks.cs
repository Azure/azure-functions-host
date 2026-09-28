// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Features;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Benchmarks;

/// <summary>
/// Measures structured-state enumeration and message formatting across logging providers.
/// </summary>
public class HttpRequestLoggingBenchmarks
{
    private ILoggerFactory _factory;
    private ILogger _logger;
    private string _route;

    /// <summary>
    /// Gets or sets the number of providers consuming each log entry.
    /// </summary>
    [Params(1, 3)]
    public int ProviderCount { get; set; }

    /// <summary>
    /// Gets or sets whether the route requires JSON escaping.
    /// </summary>
    [Params(false, true)]
    public bool EscapedRoute { get; set; }

    /// <summary>
    /// Creates the logger and verifies the benchmark exercises correct JSON formatting.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _route = EscapedRoute ? "api/products/{id:regex(^[\\d\"]+$)}" : "api/products/{category}/{id}";
        var providers = Enumerable.Range(0, ProviderCount).Select(_ => new RequestBenchmarkLoggerProvider()).ToArray();
        _factory = LoggerFactory.Create(builder =>
        {
            foreach (var provider in providers)
            {
                builder.AddProvider(provider);
            }
        });
        _logger = _factory.CreateLogger(nameof(HttpRequestLoggingBenchmarks));
        LogCompleted();
        foreach (var provider in providers)
        {
            string message = provider.Logger.Message;
            using var json = JsonDocument.Parse(message[(message.IndexOf(':') + 1)..]);
            if (!string.Equals(json.RootElement.GetProperty("route").GetString(), _route, StringComparison.Ordinal) ||
                provider.Logger.PropertyCount != 6)
            {
                throw new InvalidOperationException("The benchmark logger did not consume the expected payload and structured properties.");
            }
        }
    }

    /// <summary>
    /// Formats one completion event and exposes the original structured properties to every provider.
    /// </summary>
    [Benchmark]
    public void LogCompleted()
    {
        _logger.ExecutedHttpRequest(
            "12345678-1234-1234-1234-123456789012",
            "(WebJobsAuthLevel:Function)",
            200,
            42L,
            _route);
    }

    /// <summary>
    /// Releases the logger factory after measurement.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup() => _factory.Dispose();
}

/// <summary>
/// Measures middleware overhead with a completed downstream delegate and fixed request metadata.
/// </summary>
public class SystemTraceMiddlewareBenchmarks
{
    private ILoggerFactory _factory;
    private SystemTraceMiddleware _middleware;
    private DefaultHttpContext _context;

    /// <summary>
    /// Gets or sets the number of authenticated identities.
    /// </summary>
    [Params(0, 1, 3)]
    public int IdentityCount { get; set; }

    /// <summary>
    /// Gets or sets whether information-level request logging is enabled.
    /// </summary>
    [Params(false, true)]
    public bool LoggingEnabled { get; set; }

    /// <summary>
    /// Creates a reusable request and an allocation-free downstream delegate.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LoggingEnabled ? LogLevel.Information : LogLevel.None)
            .AddProvider(new RequestBenchmarkLoggerProvider()));
        _context = new DefaultHttpContext();
        _context.Request.Method = "GET";
        _context.Request.Headers[ScriptConstants.AntaresLogIdHeaderName] = "12345678-1234-1234-1234-123456789012";
        _context.Features.Set(new HttpRouteTemplateFeature("api/products/{category}/{id}"));
        _context.User = IdentityCount == 0
            ? new ClaimsPrincipal(new ClaimsIdentity())
            : new ClaimsPrincipal(Enumerable.Range(0, IdentityCount).Select(index =>
                new ClaimsIdentity([new Claim(SecurityConstants.AuthLevelClaimType, "Function")], $"Scheme{index}")));
        _middleware = new SystemTraceMiddleware(_ => Task.CompletedTask, _factory.CreateLogger<SystemTraceMiddleware>());
    }

    /// <summary>
    /// Runs request tracing without measuring network or function execution.
    /// </summary>
    [Benchmark]
    public Task Invoke() => _middleware.Invoke(_context);

    /// <summary>
    /// Releases the logger factory after measurement.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup() => _factory.Dispose();
}

internal sealed class RequestBenchmarkLoggerProvider : ILoggerProvider
{
    internal RequestBenchmarkLogger Logger { get; } = new();

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => Logger;

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}

internal sealed class RequestBenchmarkLogger : ILogger
{
    internal string Message { get; private set; } = string.Empty;

    internal int PropertyCount { get; private set; }

    /// <inheritdoc/>
    public IDisposable BeginScope<TState>(TState state) => NullLogger.Instance.BeginScope(state);

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        int count = 0;
        if (state is IEnumerable<KeyValuePair<string, object>> properties)
        {
            foreach (var property in properties)
            {
                count++;
            }
        }

        PropertyCount = count;
        Message = formatter(state, exception);
    }
}
