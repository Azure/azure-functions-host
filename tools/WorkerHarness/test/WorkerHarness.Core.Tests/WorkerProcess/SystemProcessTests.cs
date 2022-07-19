// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.;

using System.Diagnostics;
using WorkerHarness.Core.WorkerProcess;

namespace WorkerHarness.Core.Tests.WorkerProcess
{
    [TestClass]
    public class SystemProcessTests
    {
        [TestMethod]
        public void Start_ReturnsTrue()
        {
            // Arrange
            Process process = new();
            ProcessStartInfo startInfo = new()
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                FileName = "cmd.exe",
                Arguments = $"ls"
            };
            process.StartInfo = startInfo;

            SystemProcess systemProcess = new(process);

            // Act
            bool started = systemProcess.Start();

            // Assert
            try
            {
                Assert.IsTrue(started);
            }
            catch (AssertFailedException ex)
            {
                throw ex;
            }
            finally
            {
                process.Kill();
                process.Dispose();
            }
        }

        [TestMethod]
        public void WaitForProcessExit_HasExitedReturnsTrue()
        {
            // Arrange
            Process process = new();
            ProcessStartInfo startInfo = new()
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                FileName = "cmd.exe",
                Arguments = $"ls"
            };
            process.StartInfo = startInfo;

            SystemProcess systemProcess = new(process);
            systemProcess.Start();

            // Act
            systemProcess.WaitForProcessExit(500);

            // Assert
            try
            {
                Assert.AreEqual(process.HasExited, systemProcess.HasExited);
            }
            catch (AssertFailedException ex)
            {
                throw ex;
            }
            finally
            {
                process.Dispose();
            }
        }

        [TestMethod]
        public void Dispose_TheUnderlyingProcessIsReleased()
        {
            // Arrange
            Process process = new();
            ProcessStartInfo startInfo = new()
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                FileName = "cmd.exe",
                Arguments = $"ls"
            };
            process.StartInfo = startInfo;

            SystemProcess systemProcess = new(process);
            systemProcess.Start();

            // Act
            systemProcess.Dispose();

            // Assert
            try
            {
                int id = systemProcess.Process.Id;
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, "No process is associated with this object");
                return;
            }
            
            process.Dispose();
            Assert.Fail($"The process was not disposed because the process's Id can still be access without throwing a {typeof(InvalidOperationException)} exception");
        }
    }
}
