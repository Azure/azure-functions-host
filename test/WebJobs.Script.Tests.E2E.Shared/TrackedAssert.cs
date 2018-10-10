// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.ApplicationInsights;
using Xunit;

namespace WebJobs.Script.Tests.EndToEnd.Shared
{
    public class TrackedAssert
    {
        private readonly TelemetryClient _client;

        public TrackedAssert(TelemetryClient client)
        {
            _client = client;
        }

        public void Equals(string expected, string actual, [CallerMemberName] string testName = null, string assertionName = "")
        {
            RunAssert(testName, assertionName, () => Assert.Equal(expected, actual));
        }

        public void True(bool condition, [CallerMemberName] string testName = null, string assertionName = "")
        {
            RunAssert(testName, assertionName, () => Assert.True(condition));
        }

        private void RunAssert(string testName, string assertionName, Action assertion)
        {
            try
            {
                assertion();
            }
            catch (Exception exc)
            {
                TraceTestResult(testName, assertionName, false, exc);
                throw;
            }

            TraceTestResult(testName, assertionName, true);
        }

        private void TraceTestResult(string testName, string assertionName, bool success, Exception exc = null)
        {
            string eventName = $"{testName}.{assertionName ?? "default"}";
            string assertionId = Guid.NewGuid().ToString();
            if (exc != null)
            {
                _client.TrackException(exc, properties: new Dictionary<string, string> {
                    { "testName", testName },
                    { "assertionName", assertionName },
                    { "assertionId", assertionId } });
            }


            _client.TrackEvent("testResult", properties: new Dictionary<string, string> {
                { "testName", testName },
                { "assertionName", assertionName },
                { "assertionId", assertionId },
                { "success", bool.FalseString } });
        }
    }
}
