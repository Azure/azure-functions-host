// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait("Category", "E2E")]
    [Trait("E2E", nameof(DirectLoadEndToEndTests))]
    public class DirectLoadEndToEndTests : EndToEndTestsBase<DirectLoadEndToEndTests.TestFixture>
    {
        public DirectLoadEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact(Skip = "Fix fixture compilation issue (tracked by https://github.com/Azure/azure-webjobs-sdk-script/issues/2023)")]
        public async Task Invoke()
        {
            // Verify the type is ls in the typelocator.
            JobHostConfiguration config = this.Fixture.Host.ScriptConfig.HostConfig;
            var tl = config.TypeLocator;
            var userType = tl.GetTypes().Where(type => type.FullName == "TestFunction.DirectLoadFunction").First();
            AssertUserType(userType);

            await InvokeDotNetFunction("DotNetDirectFunction", "Hello from .NET DirectInvoker");
        }

        public async Task InvokeDotNetFunction(string functionName, string expectedResult)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://functions/myfunc");
            Dictionary<string, object> arguments = new Dictionary<string, object>()
            {
                { "req", request }
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            request.SetConfiguration(Fixture.RequestConfiguration);

            await Fixture.Host.CallAsync(functionName, arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];

            Assert.Equal(expectedResult, await response.Content.ReadAsStringAsync());
        }

        // Do validation on the type we compiled.
        // Verify that it loads and binds against the propery runtime types.
        private static void AssertUserType(Type type)
        {
            var method = type.GetMethod("Run");
            var functionNameAttr = method.GetCustomAttribute<FunctionNameAttribute>();
            Assert.NotNull(functionNameAttr);
            Assert.Equal("DotNetDirectFunction", functionNameAttr.Name);

            var parameters = method.GetParameters();
            var p1 = parameters[0];
            Assert.Equal(typeof(HttpRequestMessage), p1.ParameterType);
            var parameterAttr = p1.GetCustomAttribute<HttpTriggerAttribute>();
            Assert.NotNull(parameterAttr);
        }

        public class TestFixture : EndToEndTestFixture
        {
            private static readonly string ScriptRoot = @"TestScripts\DotNetDirect\" + Guid.NewGuid().ToString();
            private static readonly string Function1Path;

            static TestFixture()
            {
                Function1Path = Path.Combine(ScriptRoot, "DotNetDirectFunction");
                CreateFunctionAssembly();
            }

            public TestFixture() : base(ScriptRoot, "dotnet")
            {
            }

            public override void Dispose()
            {
                base.Dispose();

                FileUtility.DeleteDirectoryAsync(ScriptRoot, true).Wait();
            }

            // Create the artifacts in the ScriptRoot folder.
            private static void CreateFunctionAssembly()
            {
                Directory.CreateDirectory(Function1Path);

                uint rand = (uint)Guid.NewGuid().GetHashCode();
                string assemblyName = "Asm" + rand;
                string assemblyNamePath = assemblyName + ".dll";

                var source = GetResource("run.cs");

                var syntaxTree = CSharpSyntaxTree.ParseText(source);
                Compilation compilation = CSharpCompilation.Create(assemblyName, new[] { syntaxTree })
                    .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                    .WithReferences(
                    MetadataReference.CreateFromFile(typeof(TraceWriter).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(FunctionNameAttribute).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(HttpRequestMessage).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(HttpTriggerAttribute).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(HttpStatusCode).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

                var assemblyFullPath = Path.Combine(Function1Path, assemblyNamePath);
                var result = compilation.Emit(assemblyFullPath);
                Assert.True(result.Success);

                var hostJson = @"
{
    'id': 'function-tests-dotnet-direct'
}";
                File.WriteAllText(Path.Combine(ScriptRoot, "host.json"), hostJson);

                CreateFunctionMetadata(Function1Path, assemblyNamePath);

                // Verify that assembly loads and binds against hte same runtime types.
                {
                    Assembly assembly = Assembly.LoadFrom(assemblyFullPath);
                    var type = assembly.GetType("TestFunction.DirectLoadFunction");
                    AssertUserType(type);
                }
            }

            private static void CreateFunctionMetadata(
                string path,
                string scriptFilePath,
                string entrypoint = "TestFunction.DirectLoadFunction.Run")
            {
                var content = GetResource("function.json");
                content = string.Format(content, scriptFilePath, entrypoint);

                File.WriteAllText(Path.Combine(path, "function.json"), content);
            }

            private static string GetResource(string name)
            {
                var resourceNamespace = "Microsoft.Azure.WebJobs.Script.Tests.TestFiles.CSharp_DirectLoad.";
                var fullName = resourceNamespace + name;
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(fullName))
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
