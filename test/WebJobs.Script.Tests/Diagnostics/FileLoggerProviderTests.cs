// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class FileLoggerProviderTests
    {
        [Theory]
        [InlineData("Worker.Java.12345", @"Worker\Java")]
        [InlineData("Function.HttpTrigger", @"Function\HttpTrigger")]
        [InlineData("Function.HttpTrigger.User", @"Function\HttpTrigger")]
        [InlineData("Host.General", null)]
        public void CreateLogger_GetsCorrectPath(string category, string expectedPath)
        {
            Assert.Equal(expectedPath, FunctionFileLoggerProvider.GetFilePath(category));
        }

        [Fact]
        public void CreateLogger_UsesSameFileWriter_ForSameFile()
        {
            var options = new ScriptJobHostOptions
            {
                RootLogPath = Path.GetTempPath()
            };
            var fileStatus = new Mock<IFileLoggingStatusManager>();
            var primaryStatus = new Mock<IPrimaryHostStateProvider>();
            var fileWriterFactory = new DefaultFileWriterFactory();

            using (var provider = new FunctionFileLoggerProvider(new OptionsWrapper<ScriptJobHostOptions>(options), fileStatus.Object, primaryStatus.Object, fileWriterFactory))
            {
                provider.SetScopeProvider(new LoggerExternalScopeProvider());

                provider.CreateLogger(LogCategories.CreateFunctionCategory("Test1"));
                provider.CreateLogger(LogCategories.CreateFunctionUserCategory("Test1"));
                provider.CreateLogger(LogCategories.CreateFunctionCategory("Test1"));

                Assert.Single(provider.FileWriterCache);

                // This creates a new entry.
                provider.CreateLogger(LogCategories.CreateFunctionCategory("Test2"));

                Assert.Equal(2, provider.FileWriterCache.Count);
                Assert.NotSame(
                    provider.FileWriterCache[Path.Combine("Function", "Test1")],
                    provider.FileWriterCache[Path.Combine("Function", "Test2")]);
            }
        }
    }
}
