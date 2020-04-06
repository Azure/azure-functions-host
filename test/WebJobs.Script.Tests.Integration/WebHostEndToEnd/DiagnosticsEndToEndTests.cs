// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    public class DiagnosticsEndToEndTests
    {
        private const string _scriptRoot = @"TestScripts\CSharp";
        private readonly string _testLogPath = Path.Combine(TestHelpers.FunctionsTestDirectory, "Logs", Guid.NewGuid().ToString(), @"Functions");

        private TestEventGenerator _eventGenerator = new TestEventGenerator();

        [Fact]
        public async Task FileLogger_IOExceptionDuringInvocation_Recovers()
        {
            var fileWriterFactory = new TestFileWriterFactory(onAppendLine: null,
                onFlush: () =>
                {
                    // The below function will fail, causing an immediate flush. This exception
                    // simulates the disk being full. ExecutionEvents should be logged as expected
                    // and the "Finished" event should get logged.
                    throw new IOException();
                });

            using (var host = new TestFunctionHost(_scriptRoot, _testLogPath,
                configureWebHostServices: s =>
                {
                    s.AddSingleton<IEventGenerator>(_ => _eventGenerator);
                },
                configureScriptHostServices: s =>
                {
                    s.AddSingleton<IFileWriterFactory>(_ => fileWriterFactory);

                    s.PostConfigure<ScriptJobHostOptions>(o =>
                    {
                        o.FileLoggingMode = FileLoggingMode.Always;
                        o.Functions = new[] { "HttpTrigger-Scenarios" };
                    });
                }))
            {
                // Issue an invalid request that fails.
                var content = new StringContent(JsonConvert.SerializeObject(new { scenario = "invalid" }));
                var response = await host.HttpClient.PostAsync("/api/HttpTrigger-Scenarios", content);
                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

                await TestHelpers.Await(() =>
                {
                    var executionEvents = _eventGenerator.GetFunctionExecutionEvents();
                    return executionEvents.SingleOrDefault(p => p.ExecutionStage == ExecutionStage.Finished) != null;
                });
            }
        }

        private class TestFileWriterFactory : IFileWriterFactory
        {
            private readonly Action<string> _onAppendLine;
            private readonly Action _onFlush;

            public TestFileWriterFactory(Action<string> onAppendLine, Action onFlush)
            {
                _onAppendLine = onAppendLine;
                _onFlush = onFlush;
            }

            public IFileWriter Create(string filePath) => new TestFileWriter(_onAppendLine, _onFlush);
        }

        private class TestFileWriter : IFileWriter
        {
            private readonly Action<string> _onAppendLine;
            private readonly Action _onFlush;

            public TestFileWriter(Action<string> onAppendLine, Action onFlush)
            {
                _onAppendLine = onAppendLine;
                _onFlush = onFlush;
            }

            public void AppendLine(string line)
            {
                _onAppendLine?.Invoke(line);
            }

            public void Flush()
            {
                _onFlush();
            }
        }
    }
}
