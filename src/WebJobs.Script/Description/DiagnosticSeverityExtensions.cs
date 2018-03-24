// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public static class DiagnosticSeverityExtensions
    {
        public static LogLevel ToLogLevel(this DiagnosticSeverity severity)
        {
            var level = LogLevel.None;
            switch (severity)
            {
                case DiagnosticSeverity.Hidden:
                    level = LogLevel.Trace;
                    break;
                case DiagnosticSeverity.Info:
                    level = LogLevel.Information;
                    break;
                case DiagnosticSeverity.Warning:
                    level = LogLevel.Warning;
                    break;
                case DiagnosticSeverity.Error:
                    level = LogLevel.Error;
                    break;
            }

            return level;
        }
    }
}
