// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry
{
    internal enum TelemetryMode
    {
        None = 0, // or Default
        ApplicationInsights = 1,
        OpenTelemetry = 0b0001
    }
}
