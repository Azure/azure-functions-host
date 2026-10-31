// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Captures process startup before host and configuration construction.
/// </summary>
internal sealed record WorkerProxyStartupTime(DateTimeOffset UtcTime, long Timestamp, TimeProvider Clock)
{
    public static WorkerProxyStartupTime Capture(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new(timeProvider.GetUtcNow(), timeProvider.GetTimestamp(), timeProvider);
    }
}
