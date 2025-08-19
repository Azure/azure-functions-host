// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using BenchmarkDotNet.Attributes;
using Microsoft.Azure.WebJobs.Script.Scale;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Benchmarks
{
    public class HostPerformanceManagerBenchmarks
    {
        private readonly string _validJson = @"{""UserTime"":123,""KernelTime"":456,""PageFaults"":789,""Processes"":10,""ProcessLimit"":100,""Threads"":50,""ThreadLimit"":500}";
        private readonly string _malformedJson = @"{""UserTime"":123,""KernelTime"":456,""PageFaults"":789,""Processes"":10,""ProcessLimit"":100,""Threads"":50,""ThreadLimit"":500}abc";
        private readonly string _largeValidJson = @"{""UserTime"":123456,""KernelTime"":456789,""PageFaults"":789012,""Processes"":10,""ProcessLimit"":1000,""Threads"":50,""ThreadLimit"":5000,""Connections"":25,""ConnectionLimit"":250,""Sections"":15,""SectionLimit"":150,""NamedPipes"":5,""NamedPipeLimit"":50,""RemoteDirMonitors"":3,""RemoteDirMonitorLimit"":30,""ActiveConnections"":12,""ActiveConnectionLimit"":120,""ReadIoOperations"":1000,""WriteIoOperations"":800,""OtherIoOperations"":200,""ReadIoBytes"":5000000,""WriteIoBytes"":3000000,""OtherIoBytes"":500000,""PrivateBytes"":104857600,""Handles"":1000,""ContextSwitches"":50000,""RemoteOpens"":25}";
        private readonly string _largeMalformedJson = @"{""UserTime"":123456,""KernelTime"":456789,""PageFaults"":789012,""Processes"":10,""ProcessLimit"":1000,""Threads"":50,""ThreadLimit"":5000,""Connections"":25,""ConnectionLimit"":250,""Sections"":15,""SectionLimit"":150,""NamedPipes"":5,""NamedPipeLimit"":50,""RemoteDirMonitors"":3,""RemoteDirMonitorLimit"":30,""ActiveConnections"":12,""ActiveConnectionLimit"":120,""ReadIoOperations"":1000,""WriteIoOperations"":800,""OtherIoOperations"":200,""ReadIoBytes"":5000000,""WriteIoBytes"":3000000,""OtherIoBytes"":500000,""PrivateBytes"":104857600,""Handles"":1000,""ContextSwitches"":50000,""RemoteOpens"":25}xyz123";

        [Benchmark(Baseline = true)]
        public ApplicationPerformanceCounters ParseJson_Original_Valid()
        {
            return ParseJsonOriginal(_validJson);
        }

        [Benchmark]
        public ApplicationPerformanceCounters ParseJson_Original_Malformed()
        {
            return ParseJsonOriginal(_malformedJson);
        }

        [Benchmark]
        public ApplicationPerformanceCounters ParseJson_Optimized_Valid()
        {
            return ParseJsonOptimized(_validJson);
        }

        [Benchmark]
        public ApplicationPerformanceCounters ParseJson_Optimized_Malformed()
        {
            return ParseJsonOptimized(_malformedJson);
        }

        [Benchmark]
        public ApplicationPerformanceCounters ParseJson_Original_LargeValid()
        {
            return ParseJsonOriginal(_largeValidJson);
        }

        [Benchmark]
        public ApplicationPerformanceCounters ParseJson_Original_LargeMalformed()
        {
            return ParseJsonOriginal(_largeMalformedJson);
        }

        [Benchmark]
        public ApplicationPerformanceCounters ParseJson_Optimized_LargeValid()
        {
            return ParseJsonOptimized(_largeValidJson);
        }

        [Benchmark]
        public ApplicationPerformanceCounters ParseJson_Optimized_LargeMalformed()
        {
            return ParseJsonOptimized(_largeMalformedJson);
        }

        private ApplicationPerformanceCounters ParseJsonOriginal(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                // Original implementation with string manipulation
                int idx = json.LastIndexOf('}');
                if (idx > 0)
                {
                    json = json.Substring(0, idx + 1);
                }

                return JsonConvert.DeserializeObject<ApplicationPerformanceCounters>(json);
            }
            catch (JsonReaderException)
            {
                return null;
            }
        }

        private ApplicationPerformanceCounters ParseJsonOptimized(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                // Optimized implementation using ReadOnlySpan<char>
                ReadOnlySpan<char> jsonSpan = json.AsSpan();
                int lastBraceIndex = jsonSpan.LastIndexOf('}');
                
                if (lastBraceIndex > 0)
                {
                    jsonSpan = jsonSpan.Slice(0, lastBraceIndex + 1);
                }

                // Convert back to string only when necessary for JsonConvert
                return JsonConvert.DeserializeObject<ApplicationPerformanceCounters>(jsonSpan.ToString());
            }
            catch (JsonReaderException)
            {
                return null;
            }
        }
    }
}