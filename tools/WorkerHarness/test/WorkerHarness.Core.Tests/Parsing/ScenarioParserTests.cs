// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Moq;
using System.Data;
using WorkerHarness.Core.Parsing;

namespace WorkerHarness.Core.Tests.Parsing
{
    [TestClass]
    public class ScenarioParserTests
    {
        [TestMethod]
        public void Parse_NonExistFile_ThrowFileNotFoundException()
        {
            // Arrange
            string filePath = @"path\to\non\exist\scenario\file";
            IEnumerable<IActionProvider> actionProviders = new List<IActionProvider>()
            {
                new Mock<IActionProvider>().Object,
                new Mock<IActionProvider>().Object
            };
            ScenarioParser parser = new(actionProviders);
            string expectedExceptionMessage = string.Format(ScenarioParser.ScenarioFileNotFoundMessage, filePath);

            // Act
            try
            {
                parser.Parse(filePath);
            }
            // Assert
            catch (FileNotFoundException ex)
            {
                StringAssert.Contains(ex.Message, expectedExceptionMessage);
                return;
            }

            Assert.Fail($"The expected {typeof(FileNotFoundException)} exception is not thrown");
        }

        [TestMethod]
        public void Parse_ScenarioFileNotInJson_ThrowArgumentException()
        {
            // Arrange
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "ScenarioFileSamples\\InvalidFormat.json");
            IEnumerable<IActionProvider> actionProviders = new List<IActionProvider>()
            {
                new Mock<IActionProvider>().Object,
                new Mock<IActionProvider>().Object
            };
            ScenarioParser parser = new(actionProviders);
            string expectedExceptionMessage = string.Format(ScenarioParser.ScenarioFileNotInJsonFormat, filePath, string.Empty);

            // Act
            try
            {
                parser.Parse(filePath);
            }
            // Assert
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, expectedExceptionMessage);
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} exception is not thrown");
        }

        [TestMethod]
        [DataRow("ScenarioFileSamples\\NullActions.json")]
        [DataRow("ScenarioFileSamples\\NonArrayActions.json")]
        public void Parse_MissingActionsListInScenarioFile_ThrowArgumentException(string fileName)
        {
            // Arrange
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);

            IEnumerable<IActionProvider> actionProviders = new List<IActionProvider>()
            {
                new Mock<IActionProvider>().Object,
                new Mock<IActionProvider>().Object
            };
            ScenarioParser parser = new(actionProviders);

            string expectedExceptionMessage = string.Format(ScenarioParser.ScenarioFileMissingActionsList, filePath);

            // Act
            try
            {
                parser.Parse(filePath);
            }
            // Assert
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, expectedExceptionMessage);
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} message is not thrown.");
        }

        [TestMethod]
        public void Parse_ScenarioFileHasNoScenarioName_ScenarioNameDefaultsToFileName()
        {
            // Arrange
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "ScenarioFileSamples\\NoScenarioName.json");

            IEnumerable<IActionProvider> actionProviders = new List<IActionProvider>()
            {
                new Mock<IActionProvider>().Object,
                new Mock<IActionProvider>().Object
            };
            ScenarioParser parser = new(actionProviders);

            // Act
            Scenario scenario = parser.Parse(filePath);

            // Assert
            Assert.AreEqual("NoScenarioName.json", scenario.ScenarioName);
        }

        [TestMethod]
        public void Parse_ScenarioFileMissingActionType_ThrowArgumentException()
        {
            // Arrange
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "ScenarioFileSamples\\MissingActionType.json");

            IEnumerable<IActionProvider> actionProviders = new List<IActionProvider>()
            {
                new Mock<IActionProvider>().Object,
                new Mock<IActionProvider>().Object
            };
            ScenarioParser parser = new(actionProviders);

            string expectedExceptionMessage = string.Format(ScenarioParser.ScenarioFileMissingActionType, string.Empty);

            // Act
            try
            {
                parser.Parse(filePath);
            }
            // Assert
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, expectedExceptionMessage);
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} exception is not thrown");
        }
    }
}
