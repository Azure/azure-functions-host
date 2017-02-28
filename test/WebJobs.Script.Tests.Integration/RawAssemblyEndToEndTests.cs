﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
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
            await InvokeDotNetFunction("DotNetFunction");
        }

        [Fact]
        public async Task Invoking_DotNetFunctionShared()
        {
            await InvokeDotNetFunction("DotNetFunctionShared");
        }

        public async Task InvokeDotNetFunction(string functionName)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://functions/myfunc");
            Dictionary<string, object> arguments = new Dictionary<string, object>()
            {
                { "req", request }
            };

            await Fixture.Host.CallAsync(functionName, arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];

            Assert.Equal("Hello from .NET", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public void AssemblyChange_TriggersEnvironmentShutdown()
        {
            var manualResetEvent = new ManualResetEvent(false);
            Fixture.ScriptHostEnvironmentMock.Setup(e => e.Shutdown())
                .Callback(() => manualResetEvent.Set());

            string sourceFile = TestFixture.SharedAssemblyPath;

            File.Copy(sourceFile, Path.ChangeExtension(sourceFile, ".copy.dll"));

            bool eventSet = manualResetEvent.WaitOne(1000);

            Assert.True(eventSet, "Shutdown was not called when assembly changes were made.");
        }

        public class TestFixture : EndToEndTestFixture
        {
            private const string ScriptRoot = @"TestScripts\DotNet";
            private static readonly string FunctionPath;
            private static readonly string FunctionSharedPath;
            private static readonly string FunctionSharedBinPath;

            static TestFixture()
            {
                FunctionPath = Path.Combine(ScriptRoot, "DotNetFunction");
                FunctionSharedPath = Path.Combine(ScriptRoot, "DotNetFunctionShared");
                FunctionSharedBinPath = Path.Combine(ScriptRoot, "DotNetFunctionSharedBin");
                CreateFunctionAssembly();
            }

            public TestFixture() : base(ScriptRoot, "dotnet")
            {
            }

            public static string SharedAssemblyPath => Path.Combine(FunctionSharedBinPath, "DotNetFunctionSharedAssembly.dll");

            public override void Dispose()
            {
                base.Dispose();

                Task.WaitAll(
                    FileUtility.DeleteDirectoryAsync(FunctionPath, true),
                    FileUtility.DeleteDirectoryAsync(FunctionSharedPath, true),
                    FileUtility.DeleteDirectoryAsync(FunctionSharedBinPath, true));
            }

            private static void CreateFunctionAssembly()
            {
                Directory.CreateDirectory(FunctionPath);
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

                compilation.Emit(Path.Combine(FunctionPath, "DotNetFunctionAssembly.dll"));
                compilation.Emit(SharedAssemblyPath);

                CreateFunctionMetadata(FunctionPath, "DotNetFunctionAssembly.dll");
                CreateFunctionMetadata(FunctionSharedPath, $@"..\\{Path.GetFileName(FunctionSharedBinPath)}\\DotNetFunctionSharedAssembly.dll");
            }

            private static void CreateFunctionMetadata(string path, string scriptFilePath)
            {
                File.WriteAllText(Path.Combine(path, "function.json"),
                     string.Format(Resources.DotNetFunctionJson, scriptFilePath));
            }
        }
    }
}
