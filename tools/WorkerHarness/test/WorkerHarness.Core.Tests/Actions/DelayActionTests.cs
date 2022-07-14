// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Actions;
using WorkerHarness.Core.Variables;

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

            // Act
            try
            {
                DelayAction action = new(milisecondsDelay);
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

            // Act
            DelayAction action = new(miliseconds);

            // Assert
            Assert.AreEqual(miliseconds, action.MilisecondsDelay);
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnActionResult()
        {
            // Arrange
            int miliseconds = 500;
            DelayAction action = new(miliseconds);

            ExecutionContext context = new(new VariableManager());

            // Act
            ActionResult result = await action.ExecuteAsync(context);

            // Assert
            Assert.AreEqual(StatusCode.Success, result.Status);
        }
    }
}
