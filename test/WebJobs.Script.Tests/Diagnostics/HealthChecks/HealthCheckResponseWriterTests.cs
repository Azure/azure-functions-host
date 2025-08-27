// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics.HealthChecks
{
    public class HealthCheckResponseWriterTests
    {
        [Fact]
        public async Task WriteResponseAsync_NullHttpContext_Throws()
        {
            HealthReport report = new HealthReport(null, HealthStatus.Healthy, TimeSpan.Zero);
            await TestHelpers.Act(() =>
                HealthCheckResponseWriter.WriteResponseAsync(null, report))
                .Should().ThrowAsync<ArgumentNullException>().WithParameterName("httpContext");
        }

        [Fact]
        public async Task WriteResponseAsync_NullReport_Throws()
        {
            await TestHelpers.Act(() =>
                HealthCheckResponseWriter.WriteResponseAsync(Mock.Of<HttpContext>(), null))
                .Should().ThrowAsync<ArgumentNullException>().WithParameterName("report");
        }

        [Fact]
        public async Task WriteResponseAsync_ExpandTrue_CallsUIResponseWriter()
        {
            // Arrange
            DefaultHttpContext context = new();
            context.Request.QueryString = new QueryString("?expand=true");
            using MemoryStream stream = new();
            context.Response.Body = stream;

            Dictionary<string, HealthReportEntry> checks = new()
            {
                ["test.check.1"] = new HealthReportEntry(
                    HealthStatus.Healthy, null, TimeSpan.FromMilliseconds(10), null, null, ["test.tag.1"]),
                ["test.check.2"] = new HealthReportEntry(
                    HealthStatus.Unhealthy,
                    "Test unhealthy check",
                    TimeSpan.FromSeconds(1),
                    new Exception("Error! Error!"),
                    new Dictionary<string, object>() { ["test.data.1"] = "test value 1" },
                    ["test.tag.1", "test.tag.2"]),
            };

            HealthReport report = new(checks, TimeSpan.FromSeconds(1));

            // Act
            await HealthCheckResponseWriter.WriteResponseAsync(context, report);

            // Assert
            JsonObject expected = new()
            {
                ["status"] = "Unhealthy",
                ["totalDuration"] = "00:00:01",
                ["entries"] = new JsonObject
                {
                    ["test.check.1"] = new JsonObject
                    {
                        ["data"] = new JsonObject(),
                        ["duration"] = "00:00:00.0100000",
                        ["status"] = "Healthy",
                        ["tags"] = new JsonArray { "test.tag.1" }
                    },
                    ["test.check.2"] = new JsonObject
                    {
                        ["data"] = new JsonObject { ["test.data.1"] = "test value 1" },
                        ["description"] = "Test unhealthy check",
                        ["duration"] = "00:00:01",
                        ["exception"] = "Error! Error!",
                        ["status"] = "Unhealthy",
                        ["tags"] = new JsonArray { "test.tag.1", "test.tag.2" }
                    }
                }
            };

            stream.Position = 0;
            string actual = await new StreamReader(stream).ReadToEndAsync();
            string expectedJson = expected.ToJsonString();

            actual.Should().Be(expectedJson);
        }

        [Fact]
        public async Task WriteResponseAsync_ExpandNotTrue_WritesMinimalResponse()
        {
            // Arrange
            DefaultHttpContext context = new();
            context.Request.QueryString = new QueryString(string.Empty); // no expand
            HealthReport report = new(null, HealthStatus.Healthy, TimeSpan.Zero);
            using MemoryStream stream = new();
            context.Response.Body = stream;

            // Act
            await HealthCheckResponseWriter.WriteResponseAsync(context, report);

            // Assert
            stream.Position = 0;
            string actual = await new StreamReader(stream).ReadToEndAsync();
            string expected = "{\"status\":\"Healthy\"}";
            actual.Should().Be(expected);
        }
    }
}
