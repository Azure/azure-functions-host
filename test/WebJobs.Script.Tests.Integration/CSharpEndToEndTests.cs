// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.WindowsAzure.Storage.Blob;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait("Category", "E2E")]
    [Trait("E2E", nameof(CSharpEndToEndTests))]
    public class CSharpEndToEndTests : EndToEndTestsBase<CSharpEndToEndTests.TestFixture>
    {
        public CSharpEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task ManualTrigger_Invoke_Succeeds()
        {
            await ManualTrigger_Invoke_SucceedsTest();
        }

        [Fact]
        public async Task QueueTriggerToBlob()
        {
            await QueueTriggerToBlobTest();
        }

        [Fact]
        public async Task ServiceBusQueueTriggerToBlobTest()
        {
            await ServiceBusQueueTriggerToBlobTestImpl();
        }


        [Fact]
        public async Task TwilioReferenceInvokeSucceeds()
        {
            await TwilioReferenceInvokeSucceedsImpl(isDotNet: true);
        }

        [Fact]
        public async Task MobileTables()
        {
            await MobileTablesTest(isDotNet: true);
        }

        [Fact]
        public async Task DocumentDB()
        {
            await DocumentDBTest();
        }

        [Fact]
        public async Task NotificationHub()
        {
            await NotificationHubTest("NotificationHubOut");
        }

        [Fact]
        public async Task NotificationHub_Out_Notification()
        {
            await NotificationHubTest("NotificationHubOutNotification");
        }

        [Fact]
        public async Task NotificationHubNative()
        {
            await NotificationHubTest("NotificationHubNative");
        }

        [Fact]
        public async Task MobileTablesTable()
        {
            var id = Guid.NewGuid().ToString();
            Dictionary<string, object> arguments = new Dictionary<string, object>()
            {
                { "input",  id }
            };

            await Fixture.Host.CallAsync("MobileTableTable", arguments);

            await WaitForMobileTableRecordAsync("Item", id);
        }

        [Fact]
        public async Task FileLogging_Succeeds()
        {
            await FileLogging_SucceedsTest();
        }

        [Fact]
        public async Task MultipleOutputs()
        {
            string id1 = Guid.NewGuid().ToString();
            string id2 = Guid.NewGuid().ToString();
            string id3 = Guid.NewGuid().ToString();

            JObject input = new JObject
            {
                { "Id1", id1 },
                { "Id2", id2 },
                { "Id3", id3 }
            };
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input", input.ToString() }
            };
            await Fixture.Host.CallAsync("MultipleOutputs", arguments);

            // verify all 3 output blobs were written
            var blob = Fixture.TestOutputContainer.GetBlockBlobReference(id1);
            await TestHelpers.WaitForBlobAsync(blob);
            string blobContent = await blob.DownloadTextAsync();
            Assert.Equal("Test Blob 1", Utility.RemoveUtf8ByteOrderMark(blobContent));

            blob = Fixture.TestOutputContainer.GetBlockBlobReference(id2);
            await TestHelpers.WaitForBlobAsync(blob);
            blobContent = await blob.DownloadTextAsync();
            Assert.Equal("Test Blob 2", Utility.RemoveUtf8ByteOrderMark(blobContent));

            blob = Fixture.TestOutputContainer.GetBlockBlobReference(id3);
            await TestHelpers.WaitForBlobAsync(blob);
            blobContent = await blob.DownloadTextAsync();
            Assert.Equal("Test Blob 3", Utility.RemoveUtf8ByteOrderMark(blobContent));
        }

        [Fact]
        public async Task ScriptReference_LoadsScript()
        {
            var request = HttpTestHelpers.CreateHttpRequest("GET", "http://functions/myfunc");
            Dictionary<string, object> arguments = new Dictionary<string, object>()
            {
                { "req", request }
            };

            await Fixture.Host.CallAsync("LoadScriptReference", arguments);

            Assert.Equal("TestClass", request.HttpContext.Items["LoadedScriptResponse"]);
        }

        [Fact]
        public async Task ExecutionContext_IsPopulated()
        {
            var request = HttpTestHelpers.CreateHttpRequest("GET", "http://functions/myfunc");
            Dictionary<string, object> arguments = new Dictionary<string, object>()
            {
                { "req", request }
            };

            string functionName = "FunctionExecutionContext";
            await Fixture.Host.CallAsync(functionName, arguments);

            ExecutionContext context = request.HttpContext.Items["ContextValue"] as ExecutionContext;

            Assert.NotNull(context);
            Assert.Equal(functionName, context.FunctionName);
            Assert.Equal(Path.Combine(Fixture.Host.ScriptConfig.RootScriptPath, functionName), context.FunctionDirectory);
        }

        [Fact]
        public async Task ApiHub()
        {
            await ApiHubTest();
        }

        // TODO: FACAVAL
        //[Fact]
        //public async Task ApiHubTableClientBindingTest()
        //{
        //    var textArgValue = ApiHubTestHelper.NewRandomString();

        //    // Ensure the test entity exists.
        //    await ApiHubTestHelper.EnsureEntityAsync(ApiHubTestHelper.EntityId1);

        //    // Test table client binding.
        //    await Fixture.Host.CallAsync("ApiHubTableClient",
        //        new Dictionary<string, object>()
        //        {
        //            { ApiHubTestHelper.TextArg, textArgValue }
        //        });

        //    await ApiHubTestHelper.AssertTextUpdatedAsync(
        //        textArgValue, ApiHubTestHelper.EntityId1);
        //}

        //[Fact]
        //public async Task ApiHubTableBindingTest()
        //{
        //    var textArgValue = ApiHubTestHelper.NewRandomString();

        //    // Ensure the test entity exists.
        //    await ApiHubTestHelper.EnsureEntityAsync(ApiHubTestHelper.EntityId2);

        //    // Test table binding.
        //    TestInput input = new TestInput
        //    {
        //        Id = ApiHubTestHelper.EntityId2,
        //        Value = textArgValue
        //    };
        //    await Fixture.Host.CallAsync("ApiHubTable",
        //        new Dictionary<string, object>()
        //        {
        //            { "input", JsonConvert.SerializeObject(input) }
        //        });

        //    await ApiHubTestHelper.AssertTextUpdatedAsync(
        //        textArgValue, ApiHubTestHelper.EntityId2);
        //}

        //[Fact]
        //public async Task ApiHubTableEntityBindingTest()
        //{
        //    var textArgValue = ApiHubTestHelper.NewRandomString();

        //    // Ensure the test entity exists.
        //    await ApiHubTestHelper.EnsureEntityAsync(ApiHubTestHelper.EntityId3);

        //    // Test table entity binding.
        //    TestInput input = new TestInput
        //    {
        //        Id = ApiHubTestHelper.EntityId3,
        //        Value = textArgValue
        //    };
        //    await Fixture.Host.CallAsync("ApiHubTableEntity",
        //        new Dictionary<string, object>()
        //        {
        //            { "input", JsonConvert.SerializeObject(input) }
        //        });

        //    await ApiHubTestHelper.AssertTextUpdatedAsync(
        //        textArgValue, ApiHubTestHelper.EntityId3);
        //}

        [Fact]
        public async Task SharedAssemblyDependenciesAreLoaded()
        {
            var request = HttpTestHelpers.CreateHttpRequest("GET", "http://functions/myfunc");

            Dictionary<string, object> arguments = new Dictionary<string, object>()
            {
                { "req", request }
            };

            await Fixture.Host.CallAsync("AssembliesFromSharedLocation", arguments);

            Assert.Equal("secondary type value", request.HttpContext.Items["DependencyOutput"]);
        }

        [Fact]
        public async Task Scenario_RandGuidBinding_GeneratesRandomIDs()
        {
            var container = Fixture.BlobClient.GetContainerReference("scenarios-output");
            if (await container.ExistsAsync())
            {
                BlobResultSegment blobSegment = await container.ListBlobsSegmentedAsync(null);
                foreach (CloudBlockBlob blob in blobSegment.Results)
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

            var blobSegments = await container.ListBlobsSegmentedAsync(null);
            var blobs = blobSegments.Results.Cast<CloudBlockBlob>().ToArray();
            Assert.Equal(3, blobs.Length);
            foreach (var blob in blobs)
            {
                string content = await blob.DownloadTextAsync();
                int blobInt = int.Parse(content.Trim(new char[] { '\uFEFF', '\u200B' }));
                Assert.True(blobInt >= 0 && blobInt <= 3);
            }
        }

        [Fact]
        public async Task HttpTrigger_Post_Dynamic()
        {
            var input = new JObject
            {
                { "name", "Mathew Charles" },
                { "location", "Seattle" }
            };

            var headers = new HeaderDictionary();
            headers.Add("accept", "text/plain");

            HttpRequest request = HttpTestHelpers.CreateHttpRequest("POST", string.Format("http://localhost/api/httptrigger-dynamic"), headers, input.ToString());
            request.ContentType = "application/json";

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input", request },
                { ScriptConstants.SystemTriggerParameterName, request }
            };
            await Fixture.Host.CallAsync("HttpTrigger-Dynamic", arguments);

            var response = request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey];
            // Assert.Equal(HttpStatusCode.OK, response.);
               
            // string body = await response.Content.ReadAsStringAsync();
            // Assert.Equal("Name: Mathew Charles, Location: Seattle", body);
        }

        [Fact]
        public async Task HttpTriggerToBlob()
        {
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"http://localhost/api/HttpTriggerToBlob?Suffix=TestSuffix"),
                Method = HttpMethod.Post,
            };
            request.SetConfiguration(Fixture.RequestConfiguration);
            request.Headers.Add("Prefix", "TestPrefix");
            request.Headers.Add("Value", "TestValue");

            var id = Guid.NewGuid().ToString();
            var metadata = new JObject()
            {
                { "M1", "AAA" },
                { "M2", "BBB" }
            };
            var input = new JObject()
            {
                { "Id", id },
                { "Value", "TestInput" },
                { "Metadata", metadata }
            };
            request.Content = new StringContent(input.ToString());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            var arguments = new Dictionary<string, object>
            {
                { "input", request },
                { ScriptConstants.SystemTriggerParameterName, request }
            };
            await Fixture.Host.CallAsync("HttpTriggerToBlob", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            string expectedValue = $"TestInput{id}TestValue";
            Assert.Equal(expectedValue, body);

            // verify blob was written
            string blobName = $"TestPrefix-{id}-TestSuffix-BBB";
            var outBlob = Fixture.TestOutputContainer.GetBlockBlobReference(blobName);
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(outBlob);
            Assert.Equal(expectedValue, Utility.RemoveUtf8ByteOrderMark(result));
        }

        [Theory]
        [InlineData("application/json", "\"Name: Fabio Cavalcante, Location: Seattle\"")]
        [InlineData("application/xml", "<string xmlns=\"http://schemas.microsoft.com/2003/10/Serialization/\">Name: Fabio Cavalcante, Location: Seattle</string>")]
        [InlineData("text/plain", "Name: Fabio Cavalcante, Location: Seattle")]
        public async Task HttpTrigger_GetWithAccept_NegotiatesContent(string accept, string expectedBody)
        {
            var input = new JObject
            {
                { "name", "Fabio Cavalcante" },
                { "location", "Seattle" }
            };

            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger-dynamic")),
                Method = HttpMethod.Post,
                Content = new StringContent(input.ToString())
            };
            request.SetConfiguration(Fixture.RequestConfiguration);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input", request },
                { ScriptConstants.SystemTriggerParameterName, request }
            };

            await Fixture.Host.CallAsync("HttpTrigger-Dynamic", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(accept, response.Content.Headers.ContentType.MediaType);

            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(expectedBody, body);
        }

        public class TestFixture : EndToEndTestFixture
        {
            private const string ScriptRoot = @"TestScripts\CSharp";

            static TestFixture()
            {
                CreateSharedAssemblies();
            }

            public TestFixture() : base(ScriptRoot, "csharp")
            {
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