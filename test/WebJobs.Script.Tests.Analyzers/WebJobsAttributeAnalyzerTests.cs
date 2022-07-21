// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Xunit;
using AnalyzerTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<Microsoft.Azure.Functions.Analyzers.WebJobsAttributeAnalyzer, Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<Microsoft.Azure.Functions.Analyzers.WebJobsAttributeAnalyzer>;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using System.Collections.Immutable;
using System;
using Microsoft.Azure.Functions.Analyzers;

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
                new PackageIdentity("Microsoft.NET.Sdk.Functions", "3.1.1"),
                new PackageIdentity("Microsoft.Azure.WebJobs.Extensions.Storage", "3.0.11")));

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
                new PackageIdentity("Microsoft.NET.Sdk.Functions", "3.1.1"),
                new PackageIdentity("Microsoft.Azure.WebJobs.Extensions.Storage", "3.0.11")));

            test.TestCode = testCode;

            test.ExpectedDiagnostics.Add(Verify.Diagnostic(DiagnosticDescriptors.IllegalFunctionName)
                .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .WithSpan(12, 10, 12, 44)
                .WithArguments("90-InvalidFunction"));

            await test.RunAsync();
        }

        [Fact]
        public async Task ValidAutoResolve_NoDiagnostic()
        {
            string testCode = @"
using System.IO;
using System.Net.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
namespace FunctionApp
{
    public static class SomeFunction
    {
        public static HttpClient httpClient = new HttpClient();
        [FunctionName(""TestFunction"")]
        public static void Run([QueueTrigger(""myqueue-items"", Connection = """")] string myQueueItem,
                               [Blob(""samples-workitems/{queueTrigger}"", FileAccess.Read)] Stream myBlob,
                                ILogger log)
        {
            httpClient.GetAsync(""https://www.microsoft.com"");
        }
    }
}
";

            var test = new AnalyzerTest();
            test.ReferenceAssemblies = ReferenceAssemblies.Net.Net50.WithPackages(ImmutableArray.Create(
                new PackageIdentity("Microsoft.NET.Sdk.Functions", "3.1.1"),
                new PackageIdentity("Microsoft.Azure.WebJobs.Extensions.Storage", "3.0.11")));

            test.TestCode = testCode;

            // 0 diagnostics expected

            await test.RunAsync();
        }

        [Fact]
        public async Task InvalidAutoResolve_Diagnostic()
        {
            string badValue = "samples-workitems/{}";
            string testCode = $@"
using System.IO;
using System.Net.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
namespace FunctionApp
{{
    public static class SomeFunction
    {{
        public static HttpClient httpClient = new HttpClient();
        [FunctionName(""TestFunction"")]
        public static void Run([QueueTrigger(""myqueue-items"", Connection = """")] string myQueueItem,
                               [Blob(""{badValue}"", FileAccess.Read)] Stream myBlob,
                                ILogger log)
        {{
            httpClient.GetAsync(""https://www.microsoft.com"");
        }}
    }}
}}
";

            var test = new AnalyzerTest();
            test.ReferenceAssemblies = ReferenceAssemblies.Net.Net50.WithPackages(ImmutableArray.Create(
                new PackageIdentity("Microsoft.NET.Sdk.Functions", "3.1.1"),
                new PackageIdentity("Microsoft.Azure.WebJobs.Extensions.Storage", "3.0.11")));

            test.TestCode = testCode;

            test.ExpectedDiagnostics.Add(Verify.Diagnostic(DiagnosticDescriptors.BadBindingExpressionSyntax)
                .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithSpan(13, 38, 13, 60)
                .WithArguments("BlobPath", badValue, $"Invalid template '{badValue}'. The parameter name at position 19 is empty."));

            await test.RunAsync();
        }

        [Fact]
        public async Task ValidationFails_Diagnostic()
        {
            string badValue = "samples-workitems/myblob.";
            string testCode = $@"
using System.IO;
using System.Net.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
namespace FunctionApp
{{
    public static class SomeFunction
    {{
        public static HttpClient httpClient = new HttpClient();
        [FunctionName(""TestFunction"")]
        public static void Run([QueueTrigger(""myqueue-items"", Connection = """")] string myQueueItem,
                               [Blob(""{badValue}"", FileAccess.Read)] Stream myBlob,
                                ILogger log)
        {{
            httpClient.GetAsync(""https://www.microsoft.com"");
        }}
    }}
}}
";

            var test = new AnalyzerTest();
            test.ReferenceAssemblies = ReferenceAssemblies.Net.Net50.WithPackages(ImmutableArray.Create(
                new PackageIdentity("Microsoft.NET.Sdk.Functions", "3.1.1"),
                new PackageIdentity("Microsoft.Azure.WebJobs.Extensions.Storage", "3.0.11")));

            test.TestCode = testCode;

            test.ExpectedDiagnostics.Add(Verify.Diagnostic(DiagnosticDescriptors.FailedValidation)
                .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithSpan(13, 38, 13, 65)
                .WithArguments("BlobPath", badValue, $"The field BlobPath is invalid."));

            await test.RunAsync();
        }
    }
}
