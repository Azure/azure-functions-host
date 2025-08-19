// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using BenchmarkDotNet.Attributes;

namespace Microsoft.Azure.WebJobs.Script.Benchmarks
{
    public class StringOperationsBenchmarks
    {
        private readonly string[] _testStrings = new[]
        {
            "code",
            "admin",
            "function",
            "anonymous",
            "system",
            "user",
            "CODE",
            "ADMIN", 
            "Function",
            "",
            null
        };

        private readonly string _targetString = "function";

        [Benchmark(Baseline = true)]
        public bool StringEquals_OrdinalIgnoreCase()
        {
            bool result = false;
            foreach (var str in _testStrings)
            {
                result = string.Equals(str, _targetString, StringComparison.OrdinalIgnoreCase);
            }
            return result;
        }

        [Benchmark]
        public bool StringEquals_InvariantCultureIgnoreCase()
        {
            bool result = false;
            foreach (var str in _testStrings)
            {
                result = string.Equals(str, _targetString, StringComparison.InvariantCultureIgnoreCase);
            }
            return result;
        }

        [Benchmark]
        public bool StringEquals_CurrentCultureIgnoreCase()
        {
            bool result = false;
            foreach (var str in _testStrings)
            {
                result = string.Equals(str, _targetString, StringComparison.CurrentCultureIgnoreCase);
            }
            return result;
        }

        [Benchmark]
        public bool StringCompare_OrdinalIgnoreCase()
        {
            bool result = false;
            foreach (var str in _testStrings)
            {
                result = string.Compare(str, _targetString, StringComparison.OrdinalIgnoreCase) == 0;
            }
            return result;
        }

        [Benchmark]
        public bool StringMethod_EqualsOrdinalIgnoreCase()
        {
            bool result = false;
            foreach (var str in _testStrings)
            {
                result = str?.Equals(_targetString, StringComparison.OrdinalIgnoreCase) == true;
            }
            return result;
        }
    }
}