// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using Moq;
using WorkerHarness.Core.Actions;
using WorkerHarness.Core.Parsing;
using WorkerHarness.Core.Variables;
using WorkerHarness.Core.WorkerProcess;

namespace WorkerHarness.Core.Tests.Actions
{
    [TestClass]
    public class DelayActionTests
    {
        [TestMethod]
        public void Constructor_NegativeMilisecondsDelay_ThrowArgumentOutOfRangeException()
        {
            // Arrange
            int milisecondsDelay = -10;
            var logger = new LoggerFactory().CreateLogger<DelayAction>();

            // Act
            try
            {
                DelayAction action = new(milisecondsDelay, logger);
            }
            catch (ArgumentException ex)
            {
                Assert.AreEqual(DelayAction.InvalidArgumentMessage, ex.Message);
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} is not thrown");
        }

        [TestMethod]
        public void Constructor_PositiveMilisecondsDelay_MilisecondsDelaySet()
        {
            // Arrange
            int miliseconds = 5000;
            var logger = new LoggerFactory().CreateLogger<DelayAction>();

            // Act
            DelayAction action = new(miliseconds, logger);

            // Assert
            Assert.AreEqual(miliseconds, action.MilisecondsDelay);
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnActionResult()
        {
            // Arrange
            int miliseconds = 500;
            var logger = new LoggerFactory().CreateLogger<DelayAction>();

            DelayAction action = new(miliseconds, logger);

            ExecutionContext context = new(new VariableManager(), 
                new Mock<IScenarioParser>().Object, new Mock<IWorkerProcess>().Object);

            // Act
            ActionResult result = await action.ExecuteAsync(context);

            // Assert
            Assert.AreEqual(StatusCode.Success, result.Status);
        }
    }
}
