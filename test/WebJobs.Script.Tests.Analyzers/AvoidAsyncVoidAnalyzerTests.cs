// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Xunit;
using AnalyzerTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<Microsoft.Azure.Functions.Analyzers.AvoidAsyncVoidAnalyzer, Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<Microsoft.Azure.Functions.Analyzers.AvoidAsyncVoidAnalyzer>;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using System.Collections.Immutable;

namespace WebJobs.Script.Tests.Analyzers
{
    public class AvoidAsyncVoidAnalyzerTests
    {
        [Fact]
        public async Task AsyncVoidFunctionMethodCreatesDiagnostic()
        {
            string testCode = @"
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace FunctionApp
{
    public static class SomeFunction
    {
        [FunctionName(nameof(SomeFunction))]
        public static async void Run([QueueTrigger(""myqueue-items"", Connection = """")] string myQueueItem, ILogger log)
            {
            }
        }
    }
";
            var test = new AnalyzerTest();
            test.ReferenceAssemblies = ReferenceAssemblies.Net.Net50.WithPackages(ImmutableArray.Create(
                new PackageIdentity("Microsoft.NET.Sdk.Functions", "3.1.1"),
                new PackageIdentity("Microsoft.Azure.WebJobs.Extensions.Storage", "3.0.11")));

            test.TestCode = testCode;

            test.ExpectedDiagnostics.Add(Verify.Diagnostic().WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .WithSpan(10, 34, 10, 37));

            await test.RunAsync();
        }

        [Fact]
        public async Task AsyncTaskFunctionMethodDoesNotCreateDiagnostic()
        {
            string testCode = @"
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace FunctionApp
{
    public static class SomeFunction
    {
        [FunctionName(nameof(SomeFunction))]
        public static async Task Run([QueueTrigger(""myqueue-items"", Connection = """")] string myQueueItem, ILogger log)
            {
            }
        }
    }
";
            var test = new AnalyzerTest();
            // TODO: pull from a local source
            test.ReferenceAssemblies = ReferenceAssemblies.Net.Net50.WithPackages(ImmutableArray.Create(
                new PackageIdentity("Microsoft.NET.Sdk.Functions", "3.1.1"),
                new PackageIdentity("Microsoft.Azure.WebJobs.Extensions.Storage", "3.0.11")));

            test.TestCode = testCode;

            await test.RunAsync();
        }

        [Fact]
        public async Task AsyncVoidNotFunctionMethodDoesNotCreateDiagnostic()
        {
            string testCode = @"
namespace FunctionApp
{
    public static class NotAFunction
    {
        public static async void Run(string myQueueItem)
            {
            }
        }
    }
";
            var test = new AnalyzerTest();

            test.TestCode = testCode;

            await test.RunAsync();
        }
    }
}
