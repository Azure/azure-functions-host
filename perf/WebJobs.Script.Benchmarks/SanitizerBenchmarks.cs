// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using BenchmarkDotNet.Attributes;
using Microsoft.Azure.WebJobs.Logging;

namespace Microsoft.Azure.WebJobs.Script.Benchmarks
{
    public class SanitizerBenchmarks
    {
        [Benchmark]
        public void Sanitize()
        {
            Sanitizer.Sanitize("testprotocol://name:password@address:1111");
        }
    }
}
