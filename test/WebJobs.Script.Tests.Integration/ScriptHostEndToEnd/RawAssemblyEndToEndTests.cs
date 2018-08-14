// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.Tests.Integration.Properties;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, nameof(RawAssemblyEndToEndTests))]
    public class RawAssemblyEndToEndTests : IClassFixture<RawAssemblyEndToEndTests.TestFixture>
    {
        private TestFixture _fixture;

        public RawAssemblyEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact(Skip = "Fix fixture compilation issue (tracked by https://github.com/Azure/azure-webjobs-sdk-script/issues/2023)")]
        public async Task Invoking_DotNetFunction()
        {
            await InvokeDotNetFunction("DotNetFunction", "Hello from .NET");
        }

        [Fact(Skip = "Fix fixture compilation issue (tracked by https://github.com/Azure/azure-webjobs-sdk-script/issues/2023)")]
        public async Task Invoking_DotNetFunctionShared()
        {
            await InvokeDotNetFunction("DotNetFunctionShared", "Hello from .NET");
        }

        public async Task InvokeDotNetFunction(string functionName, string expectedResult)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://functions/myfunc");
            Dictionary<string, object> arguments = new Dictionary<string, object>()
            {
                { "req", request }
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            request.SetConfiguration(_fixture.RequestConfiguration);

            await _fixture.Host.CallAsync(functionName, arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];

            Assert.Equal(expectedResult, await response.Content.ReadAsStringAsync());
        }

        [Fact(Skip = "Fix fixture compilation issue (tracked by https://github.com/Azure/azure-webjobs-sdk-script/issues/2023)")]
        public void AssemblyChange_TriggersEnvironmentShutdown()
        {
            var manualResetEvent = new ManualResetEvent(false);
            _fixture.ScriptJobHostEnvironmentMock.Setup(e => e.Shutdown())
                .Callback(() => manualResetEvent.Set());

            string sourceFile = TestFixture.SharedAssemblyPath;

            File.Copy(sourceFile, Path.ChangeExtension(sourceFile, ".copy.dll"));

            bool eventSet = manualResetEvent.WaitOne(1000);

            Assert.True(eventSet, "Shutdown was not called when assembly changes were made.");
        }

        [Fact(Skip = "Fix fixture compilation issue (tracked by https://github.com/Azure/azure-webjobs-sdk-script/issues/2023)")]
        public async Task Invoke_WithSameTypeNames_InvokesExpectedMethod()
        {
            await InvokeDotNetFunction("Function1", "Function1");
            await InvokeDotNetFunction("Function2", "Function2");
        }

        public class TestFixture : ScriptHostEndToEndTestFixture
        {
            private const string ScriptRoot = @"TestScripts\DotNet";
            private static readonly string Function1Path;
            private static readonly string Function2Path;
            private static readonly string Function3Path;
            private static readonly string FunctionSharedPath;
            private static readonly string FunctionSharedBinPath;

            static TestFixture()
            {
                Function1Path = Path.Combine(ScriptRoot, "DotNetFunction");
                Function2Path = Path.Combine(ScriptRoot, "Function1");
                Function3Path = Path.Combine(ScriptRoot, "Function2");
                FunctionSharedPath = Path.Combine(ScriptRoot, "DotNetFunctionShared");
                FunctionSharedBinPath = Path.Combine(ScriptRoot, "DotNetFunctionSharedBin");
                CreateFunctionAssembly();
            }

            public TestFixture() : base(ScriptRoot, "dotnet", LanguageWorkerConstants.DotNetLanguageWorkerName)
            {
            }

            public static string SharedAssemblyPath => Path.Combine(FunctionSharedBinPath, "DotNetFunctionSharedAssembly.dll");

            public override async Task DisposeAsync()
            {
                await base.DisposeAsync();

                await Task.WhenAll(
                    FileUtility.DeleteDirectoryAsync(Function1Path, true),
                    FileUtility.DeleteDirectoryAsync(Function2Path, true),
                    FileUtility.DeleteDirectoryAsync(Function3Path, true),
                    FileUtility.DeleteDirectoryAsync(FunctionSharedPath, true),
                    FileUtility.DeleteDirectoryAsync(FunctionSharedBinPath, true));
            }

            private static void CreateFunctionAssembly()
            {
                Directory.CreateDirectory(Function1Path);
                Directory.CreateDirectory(Function2Path);
                Directory.CreateDirectory(Function3Path);
                Directory.CreateDirectory(FunctionSharedBinPath);
                Directory.CreateDirectory(FunctionSharedPath);

                var syntaxTree = CSharpSyntaxTree.ParseText(Resources.DotNetFunctionSource);
                Compilation compilation = CSharpCompilation.Create("DotNetFunctionAssembly", new[] { syntaxTree })
                    .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                    .WithReferences(MetadataReference.CreateFromFile(typeof(TraceWriter).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(HttpRequestMessage).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(HttpStatusCode).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

                compilation.Emit(Path.Combine(Function1Path, "DotNetFunctionAssembly.dll"));
                compilation.Emit(SharedAssemblyPath);

                CreateFunctionMetadata(Function1Path, "DotNetFunctionAssembly.dll");
                CreateFunctionMetadata(Function2Path, $@"..\\{Path.GetFileName(FunctionSharedBinPath)}\\DotNetFunctionSharedAssembly.dll", "Test.Function1.TestFunction.TestMethod");
                CreateFunctionMetadata(Function3Path, $@"..\\{Path.GetFileName(FunctionSharedBinPath)}\\DotNetFunctionSharedAssembly.dll", "Test.Function2.TestFunction.TestMethod");
                CreateFunctionMetadata(FunctionSharedPath, $@"..\\{Path.GetFileName(FunctionSharedBinPath)}\\DotNetFunctionSharedAssembly.dll");
            }

            private static void CreateFunctionMetadata(string path, string scriptFilePath, string entrypoint = "TestFunction.Function.Run")
            {
                File.WriteAllText(Path.Combine(path, "function.json"),
                     string.Format(Resources.DotNetFunctionJson, scriptFilePath, entrypoint));
            }
        }
    }
}
