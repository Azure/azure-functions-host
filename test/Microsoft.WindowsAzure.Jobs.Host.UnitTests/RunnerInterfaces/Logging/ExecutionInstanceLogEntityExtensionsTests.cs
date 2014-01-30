using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs;

namespace Microsoft.WindowsAzure.Jobs.UnitTests
{
    [TestClass]
    public class ExecutionInstanceLogEntityExtensionsTests
    {
        [TestMethod]
        public void ExecutionInstanceLogEntity_InitialState_IsAsOtherTestsAssume()
        {
            // Act
            ExecutionInstanceLogEntity entity = new ExecutionInstanceLogEntity();

            // Assert
            Assert.IsFalse(entity.HeartbeatExpires.HasValue);
            Assert.IsFalse(entity.EndTime.HasValue);
            Assert.IsNull(entity.ExceptionType);
        }

        [TestMethod]
        public void GetStatus_Initially_ReturnsRunning()
        {
            // Arrange
            ExecutionInstanceLogEntity entity = new ExecutionInstanceLogEntity();

            // Act & Assert
            TestStatus(FunctionInstanceStatus.Running, entity);
        }

        [TestMethod]
        public void GetStatus_IfEntityHasExpiredHeartbeatOnly_ReturnsNeverFinished()
        {
            // Arrange
            ExecutionInstanceLogEntity entity = new ExecutionInstanceLogEntity
            {
                HeartbeatExpires = DateTime.MinValue
            };

            // Act & Assert
            TestStatus(FunctionInstanceStatus.NeverFinished, entity);
        }

        [TestMethod]
        public void GetStatus_IfEntityHasUnexpiredHeartbeatOnly_ReturnsRunning()
        {
            // Arrange
            ExecutionInstanceLogEntity entity = new ExecutionInstanceLogEntity
            {
                HeartbeatExpires = DateTime.MaxValue
            };

            // Act & Assert
            TestStatus(FunctionInstanceStatus.Running, entity);
        }

        [TestMethod]
        public void GetStatus_IfEntityHasAllTimesThroughEndTimeWithoutException_ReturnsCompletedSuccess()
        {
            // Arrange
            ExecutionInstanceLogEntity entity = new ExecutionInstanceLogEntity
            {
                HeartbeatExpires = DateTime.MinValue,
                EndTime = DateTime.MinValue
            };

            // Act & Assert
            TestStatus(FunctionInstanceStatus.CompletedSuccess, entity);
        }

        [TestMethod]
        public void GetStatus_IfEntityHasAllTimesThroughEndTimeWithException_ReturnsCompletedFailed()
        {
            // Arrange
            ExecutionInstanceLogEntity entity = new ExecutionInstanceLogEntity
            {
                HeartbeatExpires = DateTime.MinValue,
                EndTime = DateTime.MinValue,
                ExceptionType = "Bad"
            };

            // Act & Assert
            TestStatus(FunctionInstanceStatus.CompletedFailed, entity);
        }

        [TestMethod]
        public void GetStatus_IfEntityHasOnlyEndTimeWithoutException_ReturnsCompletedSuccess()
        {
            // Arrange
            ExecutionInstanceLogEntity entity = new ExecutionInstanceLogEntity
            {
                EndTime = DateTime.MinValue
            };

            // Act & Assert
            TestStatus(FunctionInstanceStatus.CompletedSuccess, entity);
        }

        [TestMethod]
        public void GetStatus_IfEntityHasOnlyEndTimeWithException_ReturnsCompletedFailed()
        {
            // Arrange
            ExecutionInstanceLogEntity entity = new ExecutionInstanceLogEntity
            {
                EndTime = DateTime.MinValue,
                ExceptionType = "Bad"
            };

            // Act & Assert
            TestStatus(FunctionInstanceStatus.CompletedFailed, entity);
        }

        private static void TestStatus(FunctionInstanceStatus expected, ExecutionInstanceLogEntity entity)
        {
            // Act
            FunctionInstanceStatus status = ExecutionInstanceLogEntityExtensions.GetStatus(entity);

            // Assert
            Assert.AreEqual(expected, status);
        }
    }
}
