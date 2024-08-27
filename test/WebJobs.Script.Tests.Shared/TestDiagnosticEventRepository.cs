// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Tests;

public class TestDiagnosticEventRepository : IDiagnosticEventRepository
{
    private readonly List<DiagnosticEvent> _events;

    public TestDiagnosticEventRepository()
    {
        _events = new List<DiagnosticEvent>();
    }

    public List<DiagnosticEvent> Events => _events;

    public void WriteDiagnosticEvent(DateTime timestamp, string errorCode, LogLevel level, string message, string helpLink, Exception exception)
    {
        _events.Add(new DiagnosticEvent("hostid", timestamp)
        {
            ErrorCode = errorCode,
            LogLevel = level,
            Message = message,
            HelpLink = helpLink,
            Details = exception?.Message
        });
    }

    public void FlushLogs()
    {
        Events.Clear();
    }

    public bool IsEnabled()
    {
        return true;
    }
}
