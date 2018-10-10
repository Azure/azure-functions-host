// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.ApplicationInsights;

namespace WebJobs.Script.Tests.EndToEnd.Shared
{
    public static class TelemetryContext
    {
        private static readonly TelemetryClient _client;

        static TelemetryContext()
        {
            _client = new TelemetryClient();
            _client.InstrumentationKey = "7309c9d4-695a-4a94-b97b-39122efff199";
            _client.Context.Session.Id = Guid.NewGuid().ToString();
            _client.Context.Component.Version = Settings.RuntimeVersion;
        }

        public static TelemetryClient Client => _client;
    }
}
