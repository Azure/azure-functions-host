// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Colors.Net;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Newtonsoft.Json;
using NSubstitute;
using WebJobs.Script.Cli.Common;
using WebJobs.Script.Cli.Interfaces;
using WebJobs.Script.Cli.Verbs;
using Xunit;

namespace WebJobs.Script.Cli.Tests.VerbsTests
{
    public class RunVerbTests
    {
        public class TestHttpResponseMessageHandler : HttpMessageHandler
        {
            private Func<HttpRequestMessage, Task<HttpResponseMessage>> _response;

            public TestHttpResponseMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response)
            {
                _response = response;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return await _response(request);
            }
        }

        [Theory]
        [InlineData("TestFunctionName1", "test content", "Accepted", "plain/text", false)]
        [InlineData("TestFunctionName2", null, "Error", "plain/text", false)]
        [InlineData("TestFunctionName3", "{'name': 'azure'}", "Json", "application/json", false)]

        [InlineData("TestFunctionName1", "test content", "Accepted", "plain/text", true)]
        [InlineData("TestFunctionName3", "{'name': 'azure'}", "Json", "application/json", true)]
        public async Task RunVerbContentTest(string functionName, string requestContent, string responseContent, string contentType, bool fromFile)
        {
            // Setup
            var server = Substitute.For<IFunctionsLocalServer>();
            var tipsManager = Substitute.For<ITipsManager>();
            var fileSystem = Substitute.For<IFileSystem>();
            var hostStatus = new HostStatus();
            var functionStatus = new FunctionStatus
            {
                Metadata = new FunctionMetadata()
            };

            functionStatus.Metadata.Bindings.Add(new BindingMetadata { Direction = BindingDirection.In, Type = "httpTrigger" });

            FileSystemHelpers.Instance = fileSystem;

            var requestFileName = Path.GetRandomFileName();
            fileSystem.File
                .Open(Arg.Is<string>(s => s == requestFileName), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                .Returns(requestContent.ToStream());

            fileSystem.File
                .Open(Arg.Is<string>(s => s != requestFileName), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                .Returns("{'bindings':[{'type': 'httpTrigger'}]}".ToStream());

            var handler = new TestHttpResponseMessageHandler(async r =>
            {
            if (r.RequestUri.AbsolutePath == "/admin/host/status")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonConvert.SerializeObject(hostStatus), Encoding.UTF8, "application/json")
                    };
                }
                else if (r.RequestUri.AbsolutePath.StartsWith("/admin/functions/") && r.RequestUri.AbsolutePath.EndsWith("/status"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonConvert.SerializeObject(functionStatus), Encoding.UTF8, "application/json")
                    };
                }
                else
                {
                    var str = await r.Content.ReadAsStringAsync();

                    if (!string.IsNullOrEmpty(requestContent))
                    {
                        Assert.Equal(requestContent, str);
                    }

                    Assert.Equal(contentType, r.Content.Headers.ContentType.MediaType);

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(responseContent)
                    };
                }
            
            });

            var client = new HttpClient(handler)
            {
                BaseAddress = new Uri("http://localhost")
            };

            server
                .ConnectAsync(Arg.Any<TimeSpan>())
                .Returns(client);

            var stdout = Substitute.For<IConsoleWriter>();
            ColoredConsole.Out = stdout;

            var settings = Substitute.For<ISettings>();
            settings.RunFirstTimeCliExperience.Returns(false);
            settings.DisplayLaunchingRunServerWarning.Returns(false);

            // Test
            var runCommand = fromFile
                ? new RunVerb(server, tipsManager) { FileName = requestFileName, FunctionName = string.Empty }
                : new RunVerb(server, tipsManager) { Content = requestContent, FunctionName = string.Empty };

            runCommand.DependencyResolver = new DependencyResolver(new Dictionary<object, Type>() { { server, typeof(IFunctionsLocalServer) } });

            await runCommand.RunAsync();

            // Assert
            await server
                .Received()
                .ConnectAsync(Arg.Any<TimeSpan>());

            stdout
                .Received()
                .WriteLine(Arg.Is<string>(v => v == responseContent));
        }
    }
}
