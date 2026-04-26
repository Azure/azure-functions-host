// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerProxy.Supervisor;
using Xunit;

namespace Microsoft.Azure.Functions.WorkerProxy.Tests.Supervisor;

public class FunctionLogWriterTests
{
    [Theory]
    [InlineData("MS_FUNCTION_LOGS 4,,,,existing")]
    [InlineData("MS_FUNCTION_METRICS existing")]
    public void WriteProcessLine_PassesThroughTaggedLines(string taggedLine)
    {
        using var output = new StringWriter();
        var writer = CreateWriter(output);

        writer.WriteProcessLine(taggedLine);

        Assert.Equal(taggedLine + Environment.NewLine, output.ToString());
    }

    [Fact]
    public void WriteProcessLine_TagsUntaggedLines()
    {
        using var output = new StringWriter();
        var writer = CreateWriter(output);

        writer.WriteProcessLine("fatal \"boom\"");

        string line = output.ToString();
        Assert.StartsWith("MS_FUNCTION_LOGS 2,,,,workerproxy.process,workerproxy.supervisor", line);
        Assert.Contains("\"workerproxy-process: fatal 'boom'\"", line);
        Assert.Contains(",4.1050.100,2026-04-25T12:34:56.0000000Z,", line);
        Assert.Contains(",CONTAINER-01,stamp-01,tenant-01,", line);
        Assert.EndsWith(",123" + Environment.NewLine, line);
    }

    [Fact]
    public void WriteProcessLine_IgnoresEmptyUntaggedLines()
    {
        using var output = new StringWriter();
        var writer = CreateWriter(output);

        writer.WriteProcessLine(string.Empty);

        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public void WriteSupervisorMessage_WritesTaggedMessage()
    {
        using var output = new StringWriter();
        var writer = CreateWriter(output);

        writer.WriteSupervisorMessage(4, "Starting WorkerProxy");

        string line = output.ToString();
        Assert.StartsWith("MS_FUNCTION_LOGS 4,,,,workerproxy.process,workerproxy.supervisor", line);
        Assert.Contains("\"Starting WorkerProxy\"", line);
    }

    private static FunctionLogWriter CreateWriter(StringWriter output)
        => new(
            output,
            new WorkerProxySupervisorLogContext("4.1050.100", "CONTAINER-01", "stamp-01", "tenant-01"),
            () => new DateTime(2026, 4, 25, 12, 34, 56, DateTimeKind.Utc),
            "123");
}
