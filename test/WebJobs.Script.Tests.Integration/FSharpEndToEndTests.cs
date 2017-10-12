// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Xunit;
using Microsoft.WebJobs.Script.Tests;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait("Category", "E2E")]
    [Trait("E2E", nameof(FSharpEndToEndTests))]
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

        [Fact(Skip = "Migrate fixture to build the host")]
        public async Task QueueTriggerToBlob()
        {
            await QueueTriggerToBlobTest();
        }

        [Fact(Skip = "Migrate fixture to build the host")]
        public async Task ScriptReference_LoadsScript()
        {
            var request = HttpTestHelpers.CreateHttpRequest("POST", "http://some.server.com");
            Dictionary<string, object> arguments = new Dictionary<string, object>()
            {
                { "req", request }
            };

            await Fixture.Host.CallAsync("LoadScriptReference", arguments);

            Assert.Equal("TestClass", request.HttpContext.Items["LoadedScriptResponse"]);
        }

        [Fact(Skip = "Migrate fixture to build the host")]
        public async Task FileLogging_Succeeds()
        {
            await FileLogging_SucceedsTest();
        }

        [Fact(Skip = "Migrate fixture to build the host")]
        public async Task SharedAssemblyDependenciesAreLoaded()
        {
            var request = HttpTestHelpers.CreateHttpRequest("POST", "http://some.server.com");
            Dictionary<string, object> arguments = new Dictionary<string, object>()
            {
                { "req", request }
            };

            await Fixture.Host.CallAsync("AssembliesFromSharedLocation", arguments);

            Assert.Equal("secondary type value", request.HttpContext.Items["DependencyOutput"]);
        }

        [Fact(Skip = "Migrate fixture to build the host")]
        public async Task PrivateAssemblyDependenciesAreLoaded()
        {
            var request = HttpTestHelpers.CreateHttpRequest("POST", "http://some.server.com");
            Dictionary<string, object> arguments = new Dictionary<string, object>()
            {
                { "req", request }
            };

            await Fixture.Host.CallAsync("PrivateAssemblyReference", arguments);

            Assert.Equal("Test result", request.HttpContext.Items["DependencyOutput"]);
        }

        [Fact(Skip = "Migrate fixture to build the host")]
        public async Task Scenario_RandGuidBinding_GeneratesRandomIDs()
        {
            var container = Fixture.BlobClient.GetContainerReference("scenarios-output");
            if (await container.ExistsAsync())
            {
                var listResult = await container.ListBlobsSegmentedAsync(null);
                foreach (CloudBlockBlob blob in listResult.Results)
                {
                    await blob.DeleteAsync();
                }
            }

            // Call 3 times - expect 3 separate output blobs
            for (int i = 0; i < 3; i++)
            {
                ScenarioInput input = new ScenarioInput
                {
                    Scenario = "randGuid",
                    Container = "scenarios-output",
                    Value = i.ToString()
                };
                Dictionary<string, object> arguments = new Dictionary<string, object>
                {
                    { "input", JsonConvert.SerializeObject(input) }
                };
                await Fixture.Host.CallAsync("Scenarios", arguments);
            }

            var segment = await container.ListBlobsSegmentedAsync(null);

            var blobs = segment.Results.Cast<CloudBlockBlob>().ToArray();
            Assert.Equal(3, blobs.Length);
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

            public TestFixture() : base(ScriptRoot, "fsharp")
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
