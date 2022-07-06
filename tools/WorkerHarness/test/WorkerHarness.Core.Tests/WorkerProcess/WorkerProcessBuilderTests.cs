// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.WorkerProcess;
using System.Diagnostics;

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
            Process process = workerProcessBuilder.Build(languageExecutable, workerExecutable, workerDirectory);

            // Assert
            Assert.IsNotNull(process.StartInfo);
            Assert.AreEqual(process.StartInfo.FileName, languageExecutable);
            Assert.AreEqual(process.StartInfo.WorkingDirectory, workerDirectory);
            Assert.IsTrue(!string.IsNullOrEmpty(process.StartInfo.Arguments));
            Assert.IsTrue(process.StartInfo.Arguments.Contains(workerExecutable));
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
            Process process = workerProcessBuilder.Build(string.Empty, string.Empty, string.Empty);

            // Assert
            Assert.IsNotNull(process.StartInfo);
            Assert.IsTrue(!string.IsNullOrEmpty(process.StartInfo.Arguments));
            Assert.IsTrue(process.StartInfo.Arguments.Contains(hostUri));
            Assert.IsTrue(process.StartInfo.Arguments.Contains(port));
            Assert.IsTrue(process.StartInfo.Arguments.Contains(grpcMaxMessageLength));
        }

        [TestMethod]
        public void Build_ReturnProcessWhoseStartInfoContainsWorkerConstants()
        {
            // Arrange
            string workerId = WorkerConstants.WorkerId;

            // Act
            WorkerProcessBuilder workerProcessBuilder = new();
            Process process = workerProcessBuilder.Build(string.Empty, string.Empty, string.Empty);

            // Assert
            Assert.IsNotNull(process.StartInfo);
            Assert.IsTrue(!string.IsNullOrEmpty(process.StartInfo.Arguments));
            Assert.IsTrue(process.StartInfo.Arguments.Contains(workerId));
        }
    }
}