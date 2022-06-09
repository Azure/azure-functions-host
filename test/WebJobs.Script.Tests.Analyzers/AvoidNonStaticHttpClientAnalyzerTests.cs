// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Xunit;
using AnalyzerTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<Microsoft.Azure.Functions.Analyzers.AvoidNonStaticHttpClientAnalyzer, Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<Microsoft.Azure.Functions.Analyzers.AvoidNonStaticHttpClientAnalyzer>;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using System.Collections.Immutable;

namespace WebJobs.Script.Tests.Analyzers
{
    public class AvoidNonStaticHttpClientAnalyzerTests
    {
        [Fact]
        public async Task StaticHttpClient_NoDiagnostic()
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

        [FunctionName(nameof(SomeFunction))]
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
        public async Task LocalHttpClientVariable_Diagnostic()
        {
            string testCode = @"
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace FunctionApp
{
    public static class SomeFunction
    {
        [FunctionName(nameof(SomeFunction))]
        public static void Run([QueueTrigger(""myqueue-items"", Connection = """")] string myQueueItem, ILogger log)
        {
                var httpClient = new HttpClient();
        }
    }
}
";

            var test = new AnalyzerTest();
            test.ReferenceAssemblies = ReferenceAssemblies.Net.Net50.WithPackages(ImmutableArray.Create(
                new PackageIdentity("Microsoft.NET.Sdk.Functions", "3.1.1"),
                new PackageIdentity("Microsoft.Azure.WebJobs.Extensions.Storage", "3.0.11")));

            test.TestCode = testCode;

            test.ExpectedDiagnostics.Add(Verify.Diagnostic()
                .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithSpan(13, 34, 13, 50));

            await test.RunAsync();
        }

        [Fact]
        public async Task LocalNestedHttpClientVariable_Diagnostic()
        {
            string testCode = @"
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace FunctionApp
{
    public static class SomeFunction
    {
        [FunctionName(nameof(SomeFunction))]
        public static void Run([QueueTrigger(""myqueue-items"", Connection = """")] string myQueueItem, ILogger log)
        {
            if (true)
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.GetAsync(""https://www.microsoft.com"");
                }
            }
        }
    }
}
";

            var test = new AnalyzerTest();
            test.ReferenceAssemblies = ReferenceAssemblies.Net.Net50.WithPackages(ImmutableArray.Create(
                new PackageIdentity("Microsoft.NET.Sdk.Functions", "3.1.1"),
                new PackageIdentity("Microsoft.Azure.WebJobs.Extensions.Storage", "3.0.11")));

            test.TestCode = testCode;

            test.ExpectedDiagnostics.Add(Verify.Diagnostic()
                .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithSpan(15, 41, 15, 57));

            await test.RunAsync();
        }

        [Fact]
        public async Task MethodArgument_Diagnostic()
        {
            string testCode = @"
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Threading.Tasks;

namespace FunctionApp
{
    public static class SomeFunction
    {
        [FunctionName(nameof(SomeFunction))]
        public static void Run([QueueTrigger(""myqueue-items"", Connection = """")] string myQueueItem, ILogger log)
            {
                CallHttp(new HttpClient());
            }
            private static Task<HttpResponseMessage> CallHttp(HttpClient httpClient)
            {
                return httpClient.GetAsync(""https://www.microsoft.com"");
            }
        }
    }
";

            var test = new AnalyzerTest();
            test.ReferenceAssemblies = ReferenceAssemblies.Net.Net50.WithPackages(ImmutableArray.Create(
                new PackageIdentity("Microsoft.NET.Sdk.Functions", "3.1.1"),
                new PackageIdentity("Microsoft.Azure.WebJobs.Extensions.Storage", "3.0.11")));

            test.TestCode = testCode;

            test.ExpectedDiagnostics.Add(Verify.Diagnostic()
                .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithSpan(14, 26, 14, 42));

            await test.RunAsync();
        }

        [Fact]
        public async Task HttpClientDerivedClass_Diagnostic()
        {
            string testCode = @"
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace FunctionApp
{
    public static class SomeFunction
    {
        [FunctionName(nameof(SomeFunction))]
        public static void Run([QueueTrigger(""myqueue-items"", Connection = """")]string myQueueItem, ILogger log)
        {
            var httpClient = new CustomHttpClient();
            httpClient.GetAsync(""https://www.microsoft.com"");
        }
    }

    public class CustomHttpClient : HttpClient { }
}
";

            var test = new AnalyzerTest();
            test.ReferenceAssemblies = ReferenceAssemblies.Net.Net50.WithPackages(ImmutableArray.Create(
                new PackageIdentity("Microsoft.NET.Sdk.Functions", "3.1.1"),
                new PackageIdentity("Microsoft.Azure.WebJobs.Extensions.Storage", "3.0.11")));

            test.TestCode = testCode;

            test.ExpectedDiagnostics.Add(Verify.Diagnostic()
                .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithSpan(13, 30, 13, 52));

            await test.RunAsync();
        }
    }
}
