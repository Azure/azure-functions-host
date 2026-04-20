// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using Xunit;

namespace Microsoft.Azure.Functions.WorkerProxy.Tests;

public class WorkerDrainRequestTests
{
    [Fact]
    public void Deserialize_ValidReason_Succeeds()
    {
        var json = """{"reason":"IdleScaleIn"}""";
        var request = JsonSerializer.Deserialize(json, WorkerProxyJsonContext.Default.WorkerDrainRequest);

        Assert.NotNull(request);
        Assert.Equal(DrainReason.IdleScaleIn, request!.Reason);
    }

    [Fact]
    public void Deserialize_AllReasons_Succeeds()
    {
        foreach (var reason in Enum.GetValues<DrainReason>())
        {
            var json = $"{{\"reason\":\"{reason}\"}}";
            var request = JsonSerializer.Deserialize(json, WorkerProxyJsonContext.Default.WorkerDrainRequest);

            Assert.NotNull(request);
            Assert.Equal(reason, request!.Reason);
        }
    }

    [Fact]
    public void Deserialize_EmptyObject_ReasonIsNull()
    {
        var json = """{}""";
        var request = JsonSerializer.Deserialize(json, WorkerProxyJsonContext.Default.WorkerDrainRequest);

        Assert.NotNull(request);
        Assert.Null(request!.Reason);
    }

    [Fact]
    public void Deserialize_MissingReason_ReasonIsNull()
    {
        var json = """{"other":"value"}""";
        var request = JsonSerializer.Deserialize(json, WorkerProxyJsonContext.Default.WorkerDrainRequest);

        Assert.NotNull(request);
        Assert.Null(request!.Reason);
    }

    [Fact]
    public void Deserialize_InvalidReason_Throws()
    {
        var json = """{"reason":"NotAValidReason"}""";

        Assert.ThrowsAny<JsonException>(() =>
            JsonSerializer.Deserialize(json, WorkerProxyJsonContext.Default.WorkerDrainRequest));
    }
}
