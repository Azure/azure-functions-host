// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Benchmarks
{
    public class ScriptLoggingBuilderExtensionsBenchmarks
    {
        [Benchmark]
        public void Filter()
        {
            ScriptLoggingBuilderExtensions.Filter("test", LogLevel.Information, LogLevel.Information);
        }
    }
}
