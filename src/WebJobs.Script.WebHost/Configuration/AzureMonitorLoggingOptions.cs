// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    internal sealed class AzureMonitorLoggingOptions
    {
        public bool IsAzureMonitorTimeIsoFormatEnabled { get; set; }

        public string GetUtcDateTime()
        {
           return IsAzureMonitorTimeIsoFormatEnabled ? DateTime.UtcNow.ToString("s") : DateTime.UtcNow.ToString();
        }
    }
}
