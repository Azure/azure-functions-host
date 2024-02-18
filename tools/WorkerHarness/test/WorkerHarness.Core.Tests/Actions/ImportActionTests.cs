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
    public class ImportActionTests
    {
        [TestMethod]
        public async Task ExecuteAsync_AllActionsSucceed_ReturnActionResultWithStatusSuccess()
        {
            // Arrange
            var mockIAction1 = new Mock<IAction>();
            mockIAction1.Setup(x => x.ExecuteAsync(It.IsAny<ExecutionContext>()))
                .ReturnsAsync(new ActionResult() { Status = StatusCode.Success });

            var mockIAction2 = new Mock<IAction>();
            mockIAction2.Setup(x => x.ExecuteAsync(It.IsAny<ExecutionContext>()))
                .ReturnsAsync(new ActionResult() { Status = StatusCode.Success });

            var stubScenario = new Scenario("test");
            stubScenario.Actions.Add(mockIAction1.Object);
            stubScenario.Actions.Add(mockIAction2.Object);

            var mockIScenarioParser = new Mock<IScenarioParser>();
            mockIScenarioParser.Setup(x => x.Parse(It.IsAny<string>()))
                .Returns(stubScenario);

            ExecutionContext executionContext = new(
                new Mock<IVariableObservable>().Object,
                mockIScenarioParser.Object,
                new Mock<IWorkerProcess>().Object
            );

            var stubLogger = new LoggerFactory().CreateLogger<ImportAction>(); 

            ImportAction action = new("path\\to\\scenario\\file");

            // Act
            ActionResult actionResult = await action.ExecuteAsync(executionContext);

            // Assert
            Assert.AreEqual(StatusCode.Success, actionResult.Status);
        }

        [TestMethod]
        public async Task ExecuteAsync_OneActionFails_ReturnActionResultWithStatusSuccess()
        {
            // Arrange
            var mockIAction1 = new Mock<IAction>();
            mockIAction1.Setup(x => x.ExecuteAsync(It.IsAny<ExecutionContext>()))
                .ReturnsAsync(new ActionResult() { Status = StatusCode.Success });

            var mockIAction2 = new Mock<IAction>();
            mockIAction2.Setup(x => x.ExecuteAsync(It.IsAny<ExecutionContext>()))
                .ReturnsAsync(new ActionResult() { Status = StatusCode.Failure });

            var stubScenario = new Scenario("test");
            stubScenario.Actions.Add(mockIAction1.Object);
            stubScenario.Actions.Add(mockIAction2.Object);

            var mockIScenarioParser = new Mock<IScenarioParser>();
            mockIScenarioParser.Setup(x => x.Parse(It.IsAny<string>()))
                .Returns(stubScenario);

            ExecutionContext executionContext = new(
                new Mock<IVariableObservable>().Object,
                mockIScenarioParser.Object,
                new Mock<IWorkerProcess>().Object
            );

            var stubLogger = new LoggerFactory().CreateLogger<ImportAction>();

            ImportAction action = new("path\\to\\scenario\\file");

            // Act
            ActionResult actionResult = await action.ExecuteAsync(executionContext);

            // Assert
            Assert.AreEqual(StatusCode.Failure, actionResult.Status);
        }
    }
}
