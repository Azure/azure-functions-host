// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics;

/// <summary>
/// Verifies the formatting and structured properties of HTTP completion logs.
/// </summary>
public sealed class ScriptHostServiceLoggerExtensionTests
{
    /// <summary>
    /// Verifies that providers sharing a log entry observe the same cached message.
    /// </summary>
    [Fact]
    public async Task ExecutedHttpRequest_ConcurrentFormatting_ReusesMessage()
    {
        var state = new ExecutedHttpRequestLogState("request", string.Empty, 200, 42, "api/orders/{id:regex(^[\\d\"]+$)}");

        string[] messages = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(state.ToString)));

        Assert.All(messages, message => Assert.Same(messages[0], message));
        using var details = JsonDocument.Parse(messages[0][(messages[0].IndexOf(':') + 1)..]);
        Assert.Equal("api/orders/{id:regex(^[\\d\"]+$)}", details.RootElement.GetProperty("route").GetString());
    }

    /// <summary>
    /// Verifies JSON encoding while preserving the log schema and raw property values.
    /// </summary>
    [Theory]
    [InlineData("api/orders/{id}")]
    [InlineData(@"api/orders/{id:regex(^\d+$)}")]
    [InlineData("api/orders/{id:regex(^[0-9\"]+$)}")]
    [InlineData("")]
    [InlineData(null)]
    public void ExecutedHttpRequest_PreservesStructuredProperties(string route)
    {
        const string requestId = "request\"\\id";
        const string identities = "(scheme:\"level\")\n";
        var logger = new TestLogger(nameof(ScriptHostServiceLoggerExtensionTests));

        logger.ExecutedHttpRequest(requestId, identities, 200, 123L, route);

        var log = Assert.Single(logger.GetLogMessages());
        Assert.Equal(LogLevel.Information, log.Level);
        Assert.Equal(new EventId(528, nameof(ScriptHostServiceLoggerExtension.ExecutedHttpRequest)), log.EventId);
        var state = Assert.IsAssignableFrom<IReadOnlyList<KeyValuePair<string, object>>>(log.State);
        Assert.Equal(6, state.Count);
        var properties = state.ToDictionary(property => property.Key, property => property.Value);
        Assert.Equal(requestId, properties["mS_ActivityId"]);
        Assert.Equal(identities, properties[nameof(identities)]);
        Assert.Equal(200, properties["statusCode"]);
        Assert.Equal(123L, properties["duration"]);
        Assert.Equal(route, properties[nameof(route)]);
        Assert.Equal(WebHost.Properties.Resources.ExecutedHttpRequest, properties["{OriginalFormat}"]);

        using var details = JsonDocument.Parse(log.FormattedMessage[(log.FormattedMessage.IndexOf(':') + 1)..]);
        Assert.Equal(5, details.RootElement.EnumerateObject().Count());
        Assert.Equal(requestId, details.RootElement.GetProperty(nameof(requestId)).GetString());
        Assert.Equal(identities, details.RootElement.GetProperty(nameof(identities)).GetString());
        Assert.Equal("200", details.RootElement.GetProperty("status").GetString());
        Assert.Equal("123", details.RootElement.GetProperty("duration").GetString());
        Assert.Equal(route ?? "(null)", details.RootElement.GetProperty(nameof(route)).GetString());
    }

    /// <summary>
    /// Verifies that disabled completion logs do not call the logger.
    /// </summary>
    [Fact]
    public void ExecutedHttpRequest_DoesNotLog_WhenInformationIsDisabled()
    {
        var logger = new Mock<ILogger>(MockBehavior.Strict);
        logger.Setup(value => value.IsEnabled(LogLevel.Information)).Returns(false);

        logger.Object.ExecutedHttpRequest("request", string.Empty, 200, 0L, "api/orders/{id}");

        logger.Verify(value => value.IsEnabled(LogLevel.Information), Times.Once);
        logger.VerifyNoOtherCalls();
    }
}
