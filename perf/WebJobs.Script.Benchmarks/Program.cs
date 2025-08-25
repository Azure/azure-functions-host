// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using BenchmarkDotNet.Running;
using System.IO;

namespace Microsoft.Azure.WebJobs.Script.Benchmarks
{
    public static class Program
    {
        public static void Main(string[] args) =>
            BenchmarkSwitcher
                .FromAssembly(typeof(Program).Assembly)
                .Run(args, RecommendedConfig.Create(
                    artifactsPath: new DirectoryInfo(Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location), "BenchmarkDotNet.Artifacts"))
                ));
    }
}
