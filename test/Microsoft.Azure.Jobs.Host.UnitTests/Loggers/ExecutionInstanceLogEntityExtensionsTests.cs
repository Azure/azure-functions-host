using System;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Loggers
{
    public class ExecutionInstanceLogEntityExtensionsTests
    {
        [Fact]
        public void ExecutionInstanceLogEntity_InitialState_IsAsOtherTestsAssume()
        {
            // Act
            ExecutionInstanceLogEntity entity = new ExecutionInstanceLogEntity();

            // Assert
            Assert.False(entity.HeartbeatExpires.HasValue);
            Assert.False(entity.EndTime.HasValue);
            Assert.False(entity.StartTime.HasValue);
            Assert.Null(entity.ExceptionType);
        }

        [Fact]
        public void GetStatus_Initially_ReturnsQueued()
        {
            // Arrange
            ExecutionInstanceLogEntity entity = new ExecutionInstanceLogEntity();

            // Act & Assert
            TestStatus(FunctionInstanceStatus.Queued, entity);
        }

        [Fact]
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

        [Fact]
        public void GetStatus_IfEntityHasAllTimesThroughExpiredHeartbeat_ReturnsNeverFinished()
        {
            // Arrange
            ExecutionInstanceLogEntity entity = new ExecutionInstanceLogEntity
            {
                HeartbeatExpires = DateTime.MinValue,
                StartTime = DateTime.MinValue,
                QueueTime = DateTime.MinValue
            };

            // Act & Assert
            TestStatus(FunctionInstanceStatus.NeverFinished, entity);
        }

        [Fact]
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

        [Fact]
        public void GetStatus_IfEntityHasAllTimesThroughUnexpiredHeartbeat_ReturnsRunning()
        {
            // Arrange
            ExecutionInstanceLogEntity entity = new ExecutionInstanceLogEntity
            {
                HeartbeatExpires = DateTime.MaxValue,
                StartTime = DateTime.MinValue,
                QueueTime = DateTime.MinValue
            };

            // Act & Assert
            TestStatus(FunctionInstanceStatus.Running, entity);
        }

        [Fact]
        public void GetStatus_IfEntityHasAllTimesThroughEndTimeWithoutException_ReturnsCompletedSuccess()
        {
            // Arrange
            ExecutionInstanceLogEntity entity = new ExecutionInstanceLogEntity
            {
                HeartbeatExpires = DateTime.MinValue,
                EndTime = DateTime.MinValue,
                StartTime = DateTime.MinValue,
                QueueTime = DateTime.MinValue
            };

            // Act & Assert
            TestStatus(FunctionInstanceStatus.CompletedSuccess, entity);
        }

        [Fact]
        public void GetStatus_IfEntityHasAllTimesThroughEndTimeWithException_ReturnsCompletedFailed()
        {
            // Arrange
            ExecutionInstanceLogEntity entity = new ExecutionInstanceLogEntity
            {
                HeartbeatExpires = DateTime.MinValue,
                EndTime = DateTime.MinValue,
                StartTime = DateTime.MinValue,
                QueueTime = DateTime.MinValue,
                ExceptionType = "Bad"
            };

            // Act & Assert
            TestStatus(FunctionInstanceStatus.CompletedFailed, entity);
        }

        [Fact]
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

        [Fact]
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
            Assert.Equal(expected, status);
        }
    }
}
