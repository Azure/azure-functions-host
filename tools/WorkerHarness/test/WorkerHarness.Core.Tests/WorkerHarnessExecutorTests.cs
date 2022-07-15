// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Diagnostics;
using WorkerHarness.Core.Actions;
using WorkerHarness.Core.Options;
using WorkerHarness.Core.Parsing;
using WorkerHarness.Core.Variables;
using WorkerHarness.Core.WorkerProcess;

namespace WorkerHarness.Core.Tests
{
    [TestClass]
    public class WorkerHarnessExecutorTests
    {
        // 1. All Actions execute async, turn true
        [TestMethod]
        public async Task StartAsync_AllActionsExecute_ReturnTrue()
        {
            // Arrange
            var mockIWorkerProcess = new Mock<IWorkerProcess>();
            mockIWorkerProcess.Setup(x => x.Start()).Returns(true);
            mockIWorkerProcess.Setup(x => x.Kill());

            var mockIWorkerProcessBuilder = new Mock<IWorkerProcessBuilder>();
            mockIWorkerProcessBuilder
                .Setup(x => x.Build(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(mockIWorkerProcess.Object);

            var mockIAction1 = new Mock<IAction>();
            mockIAction1.Setup(x => x.ExecuteAsync(It.IsAny<ExecutionContext>()))
                .ReturnsAsync(new ActionResult());

            var mockIAction2 = new Mock<IAction>();
            mockIAction2.Setup(x => x.ExecuteAsync(It.IsAny<ExecutionContext>()))
                .ReturnsAsync(new ActionResult());

            Scenario stubScenario = new("test scenario");
            stubScenario.Actions.Add(mockIAction1.Object);
            stubScenario.Actions.Add(mockIAction2.Object);

            var mockIScenarioParser = new Mock<IScenarioParser>();
            mockIScenarioParser.Setup(s => s.Parse(It.IsAny<string>()))
                .Returns(stubScenario);

            var mockILogger = new Mock<ILogger<WorkerHarnessExecutor>>();

            var mockIOption = new Mock<IOptions<HarnessOptions>>();
            mockIOption.Setup(x => x.Value)
                .Returns(new HarnessOptions()
                {
                    ScenarioFile = "random\\file",
                    LanguageExecutable = "random\\language\\executable",
                    WorkerExecutable = "random\\worker\\executable",
                    WorkerDirectory = "random\\worker\\directory"
                });

            var mockIVariableObservable = new Mock<IVariableObservable>();

            WorkerHarnessExecutor executor = new(mockIWorkerProcessBuilder.Object,
                mockIScenarioParser.Object, mockILogger.Object, mockIOption.Object,
                mockIVariableObservable.Object);

            // Act
            bool executionResult = await executor.StartAsync();

            // Assert
            Assert.IsTrue(executionResult);
        }

        // 2. IScenarioParser throw, return false
        [TestMethod]
        public async Task StartAsync_IScenarioParserThrows_ReturnFalse()
        {
            // Arrange
            var mockIWorkerProcess = new Mock<IWorkerProcess>();
            mockIWorkerProcess.Setup(x => x.Start()).Returns(true);
            mockIWorkerProcess.Setup(x => x.Kill());

            var mockIWorkerProcessBuilder = new Mock<IWorkerProcessBuilder>();
            mockIWorkerProcessBuilder
                .Setup(x => x.Build(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(mockIWorkerProcess.Object);

            var mockIAction1 = new Mock<IAction>();
            mockIAction1.Setup(x => x.ExecuteAsync(It.IsAny<ExecutionContext>()))
                .ReturnsAsync(new ActionResult());

            var mockIAction2 = new Mock<IAction>();
            mockIAction2.Setup(x => x.ExecuteAsync(It.IsAny<ExecutionContext>()))
                .ReturnsAsync(new ActionResult());

            Scenario stubScenario = new("test scenario");

            var mockIScenarioParser = new Mock<IScenarioParser>();
            mockIScenarioParser.Setup(s => s.Parse(It.IsAny<string>()))
                .Throws(new ArgumentException("a mock exception occurs"));

            var mockILogger = new Mock<ILogger<WorkerHarnessExecutor>>();

            var mockIOption = new Mock<IOptions<HarnessOptions>>();
            mockIOption.Setup(x => x.Value)
                .Returns(new HarnessOptions()
                {
                    ScenarioFile = "random\\file",
                    LanguageExecutable = "random\\language\\executable",
                    WorkerExecutable = "random\\worker\\executable",
                    WorkerDirectory = "random\\worker\\directory"
                });

            var mockIVariableObservable = new Mock<IVariableObservable>();

            WorkerHarnessExecutor executor = new(mockIWorkerProcessBuilder.Object,
                mockIScenarioParser.Object, mockILogger.Object, mockIOption.Object,
                mockIVariableObservable.Object);

            // Act
            bool executionResult = await executor.StartAsync();

            // Assert
            Assert.IsFalse(executionResult);
        }

        // 3. An Action throw, return false
        [TestMethod]
        public async Task StartAsync_IActionThrows_ReturnFalse()
        {
            // Arrange
            var mockIWorkerProcess = new Mock<IWorkerProcess>();
            mockIWorkerProcess.Setup(x => x.Start()).Returns(true);
            mockIWorkerProcess.Setup(x => x.Kill());

            var mockIWorkerProcessBuilder = new Mock<IWorkerProcessBuilder>();
            mockIWorkerProcessBuilder
                .Setup(x => x.Build(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(mockIWorkerProcess.Object);

            var mockIAction1 = new Mock<IAction>();
            mockIAction1.Setup(x => x.ExecuteAsync(It.IsAny<ExecutionContext>()))
                .Throws(new ArgumentException("a mock exception occurs"));

            var mockIAction2 = new Mock<IAction>();
            mockIAction2.Setup(x => x.ExecuteAsync(It.IsAny<ExecutionContext>()))
                .ReturnsAsync(new ActionResult());

            Scenario stubScenario = new("test scenario");
            stubScenario.Actions.Add(mockIAction1.Object);
            stubScenario.Actions.Add(mockIAction2.Object);

            var mockIScenarioParser = new Mock<IScenarioParser>();
            mockIScenarioParser.Setup(s => s.Parse(It.IsAny<string>()))
                .Returns(stubScenario);

            var mockILogger = new Mock<ILogger<WorkerHarnessExecutor>>();

            var mockIOption = new Mock<IOptions<HarnessOptions>>();
            mockIOption.Setup(x => x.Value)
                .Returns(new HarnessOptions()
                {
                    ScenarioFile = "random\\file",
                    LanguageExecutable = "random\\language\\executable",
                    WorkerExecutable = "random\\worker\\executable",
                    WorkerDirectory = "random\\worker\\directory"
                });

            var mockIVariableObservable = new Mock<IVariableObservable>();

            WorkerHarnessExecutor executor = new(mockIWorkerProcessBuilder.Object,
                mockIScenarioParser.Object, mockILogger.Object, mockIOption.Object,
                mockIVariableObservable.Object);

            // Act
            bool executionResult = await executor.StartAsync();

            // Assert
            Assert.IsFalse(executionResult);
        }
    }
}
