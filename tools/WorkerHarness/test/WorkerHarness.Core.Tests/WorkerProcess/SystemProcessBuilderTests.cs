// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.WorkerProcess;
using System.Diagnostics;
using WorkerHarness.Core.Commons;

namespace WorkerHarness.Core.Tests.WorkerProcess
{
    [TestClass]
    public class SystemProcessBuilderTests
    {
        [TestMethod]
        public void Build_ReturnProcessWhoseStartInfoContainsInputParameters()
        {
            // Arrange
            string languageExecutable = "path\\to\\language\\executable.exe";
            string workerPath = "path\\to\\worker\\executable.exe";
            string workerDirectory = "path\\to\\worker\\directory";
            WorkerContext context = new(languageExecutable, new List<string>(),
                workerPath, new List<string>(), workerDirectory, new Uri(@"https:\\127.0.0.1:3052"));

            // Act
            SystemProcessBuilder workerProcessBuilder = new();
            IWorkerProcess process = workerProcessBuilder.Build(context);

            // Assert
            Assert.IsTrue(process is SystemProcess);
            SystemProcess systemProcess = (SystemProcess)process;

            Assert.IsNotNull(systemProcess.Process.StartInfo);
            Assert.AreEqual(systemProcess.Process.StartInfo.FileName, languageExecutable);
            Assert.AreEqual(systemProcess.Process.StartInfo.WorkingDirectory, workerDirectory);
            Assert.IsTrue(!string.IsNullOrEmpty(systemProcess.Process.StartInfo.Arguments));
            StringAssert.Contains(systemProcess.Process.StartInfo.Arguments, workerPath);
        }

        [TestMethod]
        public void Build_ReturnProcessWhoseStartInfoContainsHostConstants()
        {
            // Arrange
            string hostUri = @"https:\\127.0.0.1";
            string port = "3502";
            string grpcMaxMessageLength = int.MaxValue.ToString();
            Uri uri = new($"{hostUri}:{port}");
            WorkerContext context = new(string.Empty, new List<string>(),
                string.Empty, new List<string>(), string.Empty, uri);

            // Act
            SystemProcessBuilder workerProcessBuilder = new();
            IWorkerProcess process = workerProcessBuilder.Build(context);

            // Assert
            Assert.IsTrue(process is SystemProcess);
            SystemProcess systemProcess = (SystemProcess)process;

            Assert.IsNotNull(systemProcess.Process.StartInfo);
            Assert.IsTrue(!string.IsNullOrEmpty(systemProcess.Process.StartInfo.Arguments));
            StringAssert.Contains(systemProcess.Process.StartInfo.Arguments, "127.0.0.1");
            StringAssert.Contains(systemProcess.Process.StartInfo.Arguments, port);
            StringAssert.Contains(systemProcess.Process.StartInfo.Arguments, grpcMaxMessageLength);
        }

    }
}