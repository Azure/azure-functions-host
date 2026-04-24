// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.WorkerProxy.Configuration;
using Microsoft.Azure.Functions.WorkerProxy.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.Functions.WorkerProxy.Tests;

public class WorkerProxyFileLoggerTests
{
    private static readonly Regex FileLogRegex = new(
        "^(?<Timestamp>[^ ]+) \\[(?<Level>[^\\]]+)\\] (?<Category>[^:]+): (?<Message>.+)$",
        RegexOptions.Compiled);

    [Fact]
    public void Log_WritesHumanReadableEntry()
    {
        var lines = new List<string>();
        using var provider = new WorkerProxyFileLoggerProvider(lines.Add);
        ILogger logger = provider.CreateLogger("Microsoft.Azure.Functions.WorkerProxy.FunctionRpcRelay");
        var exception = new InvalidOperationException("bad details");

        logger.LogError(exception, "summary {Value}", 123);

        string entry = Assert.Single(lines);
        string[] segments = entry.Split(Environment.NewLine, count: 2, StringSplitOptions.None);
        Match match = FileLogRegex.Match(segments[0]);

        Assert.True(match.Success);
        Assert.Equal("Error", match.Groups["Level"].Value);
        Assert.Equal("Microsoft.Azure.Functions.WorkerProxy.FunctionRpcRelay", match.Groups["Category"].Value);
        Assert.Equal("summary 123", match.Groups["Message"].Value);
        Assert.True(DateTime.TryParse(match.Groups["Timestamp"].Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _));
        Assert.Contains(nameof(InvalidOperationException), entry);
        Assert.Contains("bad details", entry);
    }

    [Fact]
    public void Provider_AppendsToConfiguredFile()
    {
        string logFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.log");

        try
        {
            using (var provider = new WorkerProxyFileLoggerProvider(logFilePath))
            {
                ILogger logger = provider.CreateLogger("Category");

                logger.LogInformation("hello");
                logger.LogWarning("again");
            }

            string[] lines = File.ReadAllLines(logFilePath);

            Assert.Equal(2, lines.Length);
            Assert.Contains("[Information] Category: hello", lines[0], StringComparison.Ordinal);
            Assert.Contains("[Warning] Category: again", lines[1], StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(logFilePath))
            {
                File.Delete(logFilePath);
            }
        }
    }

    [Fact]
    public void LoggerFactory_FansOutToBothProviders()
    {
        var runtimeLines = new List<string>();
        var fileLines = new List<string>();

        using var runtimeProvider = new MsFunctionLogsLoggerProvider(runtimeLines.Add, Options.Create(new WorkerProxyEnvironmentOptions()));
        using var fileProvider = new WorkerProxyFileLoggerProvider(fileLines.Add);
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddProvider(runtimeProvider);
            builder.AddProvider(fileProvider);
        });

        ILogger logger = loggerFactory.CreateLogger("Category");

        logger.LogInformation("hello from factory");

        Assert.Single(runtimeLines);
        Assert.Single(fileLines);
        Assert.Contains("hello from factory", fileLines[0], StringComparison.Ordinal);
        Assert.Contains("MS_FUNCTION_LOGS", runtimeLines[0], StringComparison.Ordinal);
    }
}
