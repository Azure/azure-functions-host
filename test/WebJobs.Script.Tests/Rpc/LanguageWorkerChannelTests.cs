// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class LanguageWorkerChannelTests
    {
        [Fact]
        public void ErrorMessageQueue_Empty()
        {
            LanguageWorkerChannel languageWorkerChannel = new LanguageWorkerChannel();
            Assert.Empty(languageWorkerChannel.ProcessStdErrDataQueue);
        }

        [Fact]
        public void ErrorMessageQueue_Enqueue_Success()
        {
            LanguageWorkerChannel languageWorkerChannel = new LanguageWorkerChannel();
            languageWorkerChannel.AddStdErrMessage("Error1");
            languageWorkerChannel.AddStdErrMessage("Error2");

            Assert.True(languageWorkerChannel.ProcessStdErrDataQueue.Count == 2);
            string exceptionMessage = string.Join(",", languageWorkerChannel.ProcessStdErrDataQueue.Where(s => !string.IsNullOrEmpty(s)));
            Assert.Equal("Error1,Error2", exceptionMessage);
        }

        [Fact]
        public void ErrorMessageQueue_Full_Enqueue_Success()
        {
            LanguageWorkerChannel languageWorkerChannel = new LanguageWorkerChannel();
            languageWorkerChannel.AddStdErrMessage("Error1");
            languageWorkerChannel.AddStdErrMessage("Error2");
            languageWorkerChannel.AddStdErrMessage("Error3");
            languageWorkerChannel.AddStdErrMessage("Error4");
            Assert.True(languageWorkerChannel.ProcessStdErrDataQueue.Count == 3);
            string exceptionMessage = string.Join(",", languageWorkerChannel.ProcessStdErrDataQueue.Where(s => !string.IsNullOrEmpty(s)));
            Assert.Equal("Error2,Error3,Error4", exceptionMessage);
        }

        [Theory]
        [InlineData("languageWorkerConsoleLog Connection established")]
        [InlineData("LANGUAGEWORKERCONSOLELOG Connection established")]
        [InlineData("LanguageWorkerConsoleLog Connection established")]
        public void IsLanguageWorkerConsoleLog_Returns_True_RemovesLogPrefix(string msg)
        {
            LanguageWorkerChannel languageWorkerChannel = new LanguageWorkerChannel();
            Assert.True(languageWorkerChannel.IsLanguageWorkerConsoleLog(msg));
            Assert.Equal(" Connection established", languageWorkerChannel.RemoveLogPrefix(msg));
        }

        [Theory]
        [InlineData("grpc languageWorkerConsoleLog Connection established")]
        [InlineData("My secret languageWorkerConsoleLog")]
        [InlineData("Connection established")]
        public void IsLanguageWorkerConsoleLog_Returns_False(string msg)
        {
            LanguageWorkerChannel languageWorkerChannel = new LanguageWorkerChannel();
            Assert.False(languageWorkerChannel.IsLanguageWorkerConsoleLog(msg));
        }
    }
}
