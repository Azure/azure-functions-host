// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry
{
    internal enum TelemetryMode
    {
        ApplicationInsights = 0b0000,
        OpenTelemetry = 0b0001
    }
}
