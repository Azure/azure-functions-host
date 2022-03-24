// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Xunit;
using AnalyzerTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<Microsoft.Azure.Functions.Analyzers.WebJobsAttributeAnalyzer, Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<Microsoft.Azure.Functions.Analyzers.WebJobsAttributeAnalyzer>;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using System.Collections.Immutable;

namespace WebJobs.Script.Tests.Analyzers
{
    public class WebJobsAttributeAnalyzerTests
    {
        [Fact]
        public async Task ValidFunctionName_NoDiagnostic()
        {
            string testCode = @"
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace FunctionApp
{
    public static class SomeFunction
    {
        public static HttpClient httpClient = new HttpClient();

        [FunctionName(""ValidFunction"")]
        public static void Run([QueueTrigger(""myqueue-items"", Connection = """")] string myQueueItem, ILogger log)
        {
            httpClient.GetAsync(""https://www.microsoft.com"");
        }
    }
}
";

            var test = new AnalyzerTest();
            test.ReferenceAssemblies = ReferenceAssemblies.Net.Net50.WithPackages(ImmutableArray.Create(
                new PackageIdentity("Microsoft.NET.Sdk.Functions", "3.0.11"),
                new PackageIdentity("Microsoft.Azure.WebJobs.Extensions.Storage", "3.0.10")));

            test.TestCode = testCode;

            // 0 diagnostics expected

            await test.RunAsync();
        }

        [Fact]
        public async Task InvalidFunctionName_Diagnostic()
        {
            string testCode = @"
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace FunctionApp
{
    public static class SomeFunction
    {
        public static HttpClient httpClient = new HttpClient();

        [FunctionName(""90-InvalidFunction"")]
        public static void Run([QueueTrigger(""myqueue-items"", Connection = """")] string myQueueItem, ILogger log)
        {
            httpClient.GetAsync(""https://www.microsoft.com"");
        }
    }
}
";

            var test = new AnalyzerTest();
            test.ReferenceAssemblies = ReferenceAssemblies.Net.Net50.WithPackages(ImmutableArray.Create(
                new PackageIdentity("Microsoft.NET.Sdk.Functions", "3.0.11"),
                new PackageIdentity("Microsoft.Azure.WebJobs.Extensions.Storage", "3.0.10")));

            test.TestCode = testCode;

            test.ExpectedDiagnostics.Add(Verify.Diagnostic()
                .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .WithSpan(12, 10, 12, 44)
                .WithArguments("90-InvalidFunction"));

            await test.RunAsync();
        }
    }
}
