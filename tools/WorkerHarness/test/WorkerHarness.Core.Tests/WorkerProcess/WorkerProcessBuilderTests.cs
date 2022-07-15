// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.WorkerProcess;
using System.Diagnostics;
using WorkerHarness.Core.Commons;

namespace WorkerHarness.Core.Tests.WorkerProcess
{
    [TestClass]
    public class WorkerProcessBuilderTests
    {
        [TestMethod]
        public void Build_ReturnProcessWhoseStartInfoContainsInputParameters()
        {
            // Arrange
            string languageExecutable = "path\\to\\language\\executable.exe";
            string workerExecutable = "path\\to\\worker\\executable.exe";
            string workerDirectory = "path\\to\\worker\\directory";

            // Act
            WorkerProcessBuilder workerProcessBuilder = new();
            IWorkerProcess process = workerProcessBuilder.Build(languageExecutable, workerExecutable, workerDirectory);

            // Assert
            Assert.IsTrue(process is SystemProcess);
            SystemProcess systemProcess = (SystemProcess)process;

            Assert.IsNotNull(systemProcess.Process.StartInfo);
            Assert.AreEqual(systemProcess.Process.StartInfo.FileName, languageExecutable);
            Assert.AreEqual(systemProcess.Process.StartInfo.WorkingDirectory, workerDirectory);
            Assert.IsTrue(!string.IsNullOrEmpty(systemProcess.Process.StartInfo.Arguments));
            StringAssert.Contains(systemProcess.Process.StartInfo.Arguments, workerExecutable);
        }

        [TestMethod]
        public void Build_ReturnProcessWhoseStartInfoContainsHostConstants()
        {
            // Arrange
            string hostUri = HostConstants.DefaultHostUri;
            string port = HostConstants.DefaultPort.ToString();
            string grpcMaxMessageLength = HostConstants.GrpcMaxMessageLength.ToString();

            // Act
            WorkerProcessBuilder workerProcessBuilder = new();
            IWorkerProcess process = workerProcessBuilder.Build(string.Empty, string.Empty, string.Empty);

            // Assert
            Assert.IsTrue(process is SystemProcess);
            SystemProcess systemProcess = (SystemProcess)process;

            Assert.IsNotNull(systemProcess.Process.StartInfo);
            Assert.IsTrue(!string.IsNullOrEmpty(systemProcess.Process.StartInfo.Arguments));
            StringAssert.Contains(systemProcess.Process.StartInfo.Arguments, hostUri);
            StringAssert.Contains(systemProcess.Process.StartInfo.Arguments, port);
            StringAssert.Contains(systemProcess.Process.StartInfo.Arguments, grpcMaxMessageLength);
        }

        [TestMethod]
        public void Build_ReturnProcessWhoseStartInfoContainsWorkerConstants()
        {
            // Arrange
            string workerId = WorkerConstants.WorkerId;

            // Act
            WorkerProcessBuilder workerProcessBuilder = new();
            IWorkerProcess process = workerProcessBuilder.Build(string.Empty, string.Empty, string.Empty);

            // Assert
            Assert.IsTrue(process is SystemProcess);
            SystemProcess systemProcess = (SystemProcess)process;

            Assert.IsNotNull(systemProcess.Process.StartInfo);
            Assert.IsTrue(!string.IsNullOrEmpty(systemProcess.Process.StartInfo.Arguments));
            StringAssert.Contains(systemProcess.Process.StartInfo.Arguments, workerId);
        }
    }
}