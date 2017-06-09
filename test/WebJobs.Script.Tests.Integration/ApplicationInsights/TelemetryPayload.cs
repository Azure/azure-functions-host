// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Tests.ApplicationInsights
{
    public class TelemetryPayload
    {
        public string Name { get; set; }

        public DateTime Time { get; set; }

        public string IKey { get; set; }

        public IDictionary<string, string> Tags { get; } = new Dictionary<string, string>();

        public TelemetryData Data { get; } = new TelemetryData();

        public override string ToString()
        {
            return Data.BaseData.Message;
        }
    }

    public class TelemetryData
    {
        public string BaseType { get; set; }

        public TelemetryBaseData BaseData { get; } = new TelemetryBaseData();
    }

    public class TelemetryBaseData
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public TimeSpan Duration { get; set; }

        public bool? Success { get; set; }

        public string Ver { get; set; }

        public string Message { get; set; }

        public string SeverityLevel { get; set; }

        public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>();
    }
}
