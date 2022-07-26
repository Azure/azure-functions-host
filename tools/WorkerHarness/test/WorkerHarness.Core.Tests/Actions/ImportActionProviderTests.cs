// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json.Nodes;
using WorkerHarness.Core.Actions;

namespace WorkerHarness.Core.Tests.Actions
{
    [TestClass]
    public class ImportActionProviderTests
    {
        [TestMethod]
        public void Create_ActionNodeMissingScenarioFile_ThrowArgumentException()
        {
            // Arrange
            JsonNode stubNode = new JsonObject
            {
                ["actionType"] = "import"
            };

            var mockILoggerFactory = new Mock<ILoggerFactory>();

            ImportActionProvider provider = new(mockILoggerFactory.Object);

            // Act
            try
            {
                provider.Create(stubNode);
            }
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, ImportActionProvider.MissingScenarioFileException);
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} is not thrown");
        }

        [TestMethod]
        public void Create_ActionNodeHasNonexistingScenarioFile_ThrowArgumentException()
        {
            // Arrange
            JsonNode stubNode = new JsonObject
            {
                ["actionType"] = "import",
                ["scenarioFile"] = "path\\to\\non\\existing\\file"
            };

            var mockILoggerFactory = new Mock<ILoggerFactory>();

            ImportActionProvider provider = new(mockILoggerFactory.Object);

            // Act
            try
            {
                provider.Create(stubNode);
            }
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, ImportActionProvider.ScenarioFileDoesNotExist);
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} is not thrown");
        }

        [TestMethod]
        public void Create_ActionNodeContainsAbsoluteScenarioPath_ReturnImportAction()
        {
            // Arrange
            string scenarioPath = Path.Combine(Directory.GetCurrentDirectory(), @"ScenarioFileSamples\ValidScenario.json");
            JsonNode stubNode = new JsonObject
            {
                ["actionType"] = "import",
                ["scenarioFile"] = scenarioPath
            };

            var mockILoggerFactory = new Mock<ILoggerFactory>();

            ImportActionProvider provider = new(mockILoggerFactory.Object);

            // Act
            IAction action = provider.Create(stubNode);

            // Assert
            Assert.AreEqual(ActionTypes.Import, provider.Type);
            Assert.IsTrue(action is ImportAction);
            ImportAction importAction = (ImportAction)action;
            Assert.AreEqual(scenarioPath, importAction.ScenarioFile);
            Assert.AreEqual(ActionTypes.Import, importAction.Type);
        }

        [TestMethod]
        public void Create_ActionNodeContainsRelativeScenarioPath_ReturnImportAction()
        {
            // Arrange
            string scenarioPath = @"ScenarioFileSamples\ValidScenario.json";
            JsonNode stubNode = new JsonObject
            {
                ["actionType"] = "import",
                ["scenarioFile"] = scenarioPath
            };

            var mockILoggerFactory = new Mock<ILoggerFactory>();

            ImportActionProvider provider = new(mockILoggerFactory.Object);

            string expectedpath = Path.Combine(Directory.GetCurrentDirectory(), scenarioPath);

            // Act
            IAction action = provider.Create(stubNode);

            // Assert
            Assert.AreEqual(ActionTypes.Import, provider.Type);
            Assert.IsTrue(action is ImportAction);
            ImportAction importAction = (ImportAction)action;
            Assert.IsTrue(Path.IsPathRooted(importAction.ScenarioFile));
            Assert.AreEqual(expectedpath, importAction.ScenarioFile);
            Assert.AreEqual(ActionTypes.Import, importAction.Type);
        }
    }
}
