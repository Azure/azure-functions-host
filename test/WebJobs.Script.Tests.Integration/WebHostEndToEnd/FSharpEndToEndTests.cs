// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, nameof(FSharpEndToEndTests))]
    public class FSharpEndToEndTests : EndToEndTestsBase<FSharpEndToEndTests.TestFixture>
    {
        public FSharpEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact(Skip = "Fix dependency compilation")]
        public async Task ManualTrigger_Invoke_Succeeds()
        {
            await ManualTrigger_Invoke_SucceedsTest();
        }

        [Fact(Skip = "Not yet enabled.")]
        public void QueueTriggerToBlob()
        {
            // await QueueTriggerToBlobTest();
        }

        [Fact]
        public async Task ScriptReference_LoadsScript()
        {
            HttpResponseMessage response = await Fixture.Host.HttpClient.GetAsync($"api/LoadScriptReference");
            response.EnsureSuccessStatusCode();
            Assert.Equal("TestClass", await response.Content.ReadAsStringAsync());
        }

        [Fact(Skip = "Not yet enabled.")]
        public void FunctionLogging_Succeeds()
        {
            // await FunctionLogging_SucceedsTest();
        }

        [Fact]
        public async Task SharedAssemblyDependenciesAreLoaded()
        {
            HttpResponseMessage response = await Fixture.Host.HttpClient.GetAsync($"api/AssembliesFromSharedLocation");
            Assert.Equal("secondary type value", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task PrivateAssemblyDependenciesAreLoaded()
        {
            HttpResponseMessage response = await Fixture.Host.HttpClient.GetAsync($"api/PrivateAssemblyReference");
            Assert.Equal("Test result", await response.Content.ReadAsStringAsync());
        }

        [Fact(Skip = "Function is not compiling.")]
        public async Task RandGuidBinding_GeneratesRandomIDs()
        {
            var blobs = await Scenario_RandGuidBinding_GeneratesRandomIDs();

            foreach (var blob in blobs)
            {
                string content = await blob.DownloadTextAsync();
                int blobInt = int.Parse(content.Trim(new char[] { '\uFEFF', '\u200B' }));
                Assert.True(blobInt >= 0 && blobInt <= 3);
            }
        }

        public class TestFixture : EndToEndTestFixture
        {
            private const string ScriptRoot = @"TestScripts\FSharp";

            static TestFixture()
            {
                CreateTestDependency();
                CreateSharedAssemblies();
            }

            public TestFixture() : base(ScriptRoot, "fsharp", LanguageWorkerConstants.DotNetLanguageWorkerName)
            {
            }

            private static void CreateTestDependency()
            {
                string assemblyPath = Path.Combine(ScriptRoot, @"PrivateAssemblyReference\bin");

                if (Directory.Exists(assemblyPath))
                {
                    Directory.Delete(assemblyPath, true);
                }

                Directory.CreateDirectory(assemblyPath);

                string primaryReferenceSource = @"
namespace TestDependency
{
    public class TestClass
    {
        public string GetValue()
        {
            return ""Test result"";
        }
    }
}";

                var primarySyntaxTree = CSharpSyntaxTree.ParseText(primaryReferenceSource);
                Compilation compilation = CSharpCompilation.Create("TestDependency", new[] { primarySyntaxTree })
                    .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                    .WithReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

                compilation.Emit(Path.Combine(assemblyPath, "TestDependency.dll"));
            }

            private static void CreateSharedAssemblies()
            {
                string sharedAssembliesPath = Path.Combine(ScriptRoot, "SharedAssemblies");

                if (Directory.Exists(sharedAssembliesPath))
                {
                    Directory.Delete(sharedAssembliesPath, true);
                }

                Directory.CreateDirectory(sharedAssembliesPath);

                string secondaryDependencyPath = Path.Combine(sharedAssembliesPath, "SecondaryDependency.dll");

                string primaryReferenceSource = @"
using SecondaryDependency;

namespace PrimaryDependency
{
    public class Primary
    {
        public string GetValue()
        {
            var secondary = new Secondary();
            return secondary.GetSecondaryValue();
        }
    }
}";
                string secondaryReferenceSource = @"
namespace SecondaryDependency
{
    public class Secondary
    {
        public string GetSecondaryValue()
        {
            return ""secondary type value"";
        }
    }
}";
                var secondarySyntaxTree = CSharpSyntaxTree.ParseText(secondaryReferenceSource);
                Compilation secondaryCompilation = CSharpCompilation.Create("SecondaryDependency", new[] { secondarySyntaxTree })
                    .WithReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                    .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                secondaryCompilation.Emit(secondaryDependencyPath);

                var primarySyntaxTree = CSharpSyntaxTree.ParseText(primaryReferenceSource);
                Compilation primaryCompilation = CSharpCompilation.Create("PrimaryDependency", new[] { primarySyntaxTree })
                    .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                    .WithReferences(MetadataReference.CreateFromFile(secondaryDependencyPath), MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

                primaryCompilation.Emit(Path.Combine(sharedAssembliesPath, "PrimaryDependency.dll"));
            }
        }

        public class TestInput
        {
            public int Id { get; set; }

            public string Value { get; set; }
        }
    }
}
