// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Tests.Properties;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class RawAssemblyEndToEndTests : EndToEndTestsBase<RawAssemblyEndToEndTests.TestFixture>
    {
        public RawAssemblyEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task Invoking_DotNetFunction()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://functions/myfunc");
            Dictionary<string, object> arguments = new Dictionary<string, object>()
            {
                { "req", request }
            };

            await Fixture.Host.CallAsync("DotNetFunction", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];

            Assert.Equal("Hello from .NET", await response.Content.ReadAsStringAsync());
        }

        public class TestFixture : EndToEndTestFixture
        {
            private const string ScriptRoot = @"TestScripts\DotNet";
            private static readonly string FunctionPath;

            static TestFixture()
            {
                FunctionPath = Path.Combine(ScriptRoot, "DotNetFunction");
                CreateFunctionAssembly();
            }

            public TestFixture() : base(ScriptRoot, "dotnet")
            {
            }

            public override void Dispose()
            {
                base.Dispose();

                if (Directory.Exists(FunctionPath))
                {
                    Directory.Delete(FunctionPath, true);
                }
            }

            private static void CreateFunctionAssembly()
            {
                Directory.CreateDirectory(FunctionPath);

                var syntaxTree = CSharpSyntaxTree.ParseText(Resources.DotNetFunctionSource);
                Compilation compilation = CSharpCompilation.Create("DotNetFunctionAssembly", new[] { syntaxTree })
                    .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                    .WithReferences(MetadataReference.CreateFromFile(typeof(TraceWriter).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(HttpRequestMessage).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(HttpStatusCode).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

                var result = compilation.Emit(Path.Combine(FunctionPath, "DotNetFunctionAssembly.dll"));

                // Create function metadata
                File.WriteAllText(Path.Combine(FunctionPath, "function.json"), Resources.DotNetFunctionJson);
            }
        }
    }
}
