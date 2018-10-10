// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Xunit.Sdk;

namespace WebJobs.Script.Tests.EndToEnd.Shared
{
    public class TestTraceAttribute : BeforeAfterTestAttribute
    {
        private readonly Stopwatch _stopWatch;

        public TestTraceAttribute()
        {
            _stopWatch = new Stopwatch();
        }
        public override void Before(MethodInfo methodUnderTest)
        {
            base.Before(methodUnderTest);

            _stopWatch.Start();
        }

        public override void After(MethodInfo methodUnderTest)
        {
            base.After(methodUnderTest);
            _stopWatch.Stop();

            TelemetryContext.Client.TrackMetric("TestDuration", _stopWatch.ElapsedMilliseconds,
                new Dictionary<string, string> { { "TestName", $"{methodUnderTest.DeclaringType.Name}.{methodUnderTest.Name}" } });
        }
    }
}
