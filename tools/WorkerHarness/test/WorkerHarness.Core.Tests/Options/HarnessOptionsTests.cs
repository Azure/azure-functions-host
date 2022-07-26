// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
using Microsoft.Extensions.Logging;
using WorkerHarness.Core.Options;

namespace WorkerHarness.Core.Tests.Options
{
    [TestClass]
    public class HarnessOptionsTests
    {
        private readonly ILogger<HarnessOptionsValidate> stubLogger = new Logger<HarnessOptionsValidate>(new LoggerFactory());

        [TestMethod]
        public void Validate_HarnessOptionsWithValidPath_ReturnTrue()
        {
            // Arrange
            string directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "ScenarioFileSamples");
            string[] stubFiles = Directory.GetFiles(directoryPath);

            HarnessOptions options = new()
            {
                ScenarioFile = stubFiles[0],
                LanguageExecutable = stubFiles[1],
                WorkerExecutable = stubFiles[2],
                WorkerDirectory = Directory.GetCurrentDirectory()
            };
            IHarnessOptionsValidate harnessOptionsValidate = new HarnessOptionsValidate(stubLogger);

            // Act
            bool actual = harnessOptionsValidate.Validate(options);

            // Assert
            Assert.IsTrue(actual);
            Assert.IsTrue(Path.IsPathRooted(options.ScenarioFile));
            Assert.IsTrue(Path.IsPathRooted(options.LanguageExecutable));
            Assert.IsTrue(Path.IsPathRooted(options.WorkerExecutable));
            Assert.IsTrue(Path.IsPathRooted(options.WorkerDirectory));
        }

        [TestMethod]
        public void Validate_HarnessOptionsWithNullPaths_ReturnFalse()
        {
            // Arrange
            HarnessOptions options = new();
            IHarnessOptionsValidate harnessOptionsValidate = new HarnessOptionsValidate(stubLogger);

            // Act
            bool actual = harnessOptionsValidate.Validate(options);

            // Assert
            Assert.IsFalse(actual);
        }

        [TestMethod]
        public void Validate_HarnessOptionsWithNonExistingFiles_ReturnFalse()
        {
            // Arrange
            HarnessOptions options = new()
            {
                ScenarioFile = @"path\to\a\scenario\file",
                LanguageExecutable = @"path\to\a\language\executable",
                WorkerExecutable = @"path\to\a\language\worker\executable",
                WorkerDirectory = Directory.GetCurrentDirectory()
            };
            IHarnessOptionsValidate harnessOptionsValidate = new HarnessOptionsValidate(stubLogger);

            // Act
            bool actual = harnessOptionsValidate.Validate(options);

            // Assert
            Assert.IsFalse(actual);
        }

        [TestMethod]
        [DataRow(@"ScenarioFileSamples\ValidScenario.json")]
        [DataRow(@"ScenarioFileSamples\NoScenarioName.json")]
        public void Validate_HarnessOptionsWithRelativeScenarioFileThatExists_ReturnTrue(string relativeScenarioPath)
        {
            // Arrange
            string directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "ScenarioFileSamples");
            string[] stubFiles = Directory.GetFiles(directoryPath);

            HarnessOptions options = new()
            {
                ScenarioFile = relativeScenarioPath,
                LanguageExecutable = stubFiles[0],
                WorkerExecutable = stubFiles[1],
                WorkerDirectory = directoryPath
            };
            IHarnessOptionsValidate harnessOptionsValidate = new HarnessOptionsValidate(stubLogger);

            string expectedScenarioPath = Path.Combine(Directory.GetCurrentDirectory(), relativeScenarioPath);

            // Act
            bool actual = harnessOptionsValidate.Validate(options);

            // Assert
            Assert.IsTrue(actual);
            Assert.IsTrue(Path.IsPathRooted(options.ScenarioFile));
            Assert.AreEqual(expectedScenarioPath, options.ScenarioFile);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow(@"ScenarioFileSamples\NotExistingScenario.json")]
        public void Validate_HarnessOptionsWithRelativeScenarioFileThatNotExists_ReturnFalse(string relativeScenarioPath)
        {
            // Arrange
            string directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "ScenarioFileSamples");
            string[] stubFiles = Directory.GetFiles(directoryPath);

            HarnessOptions options = new()
            {
                ScenarioFile = relativeScenarioPath,
                LanguageExecutable = stubFiles[0],
                WorkerExecutable = stubFiles[1],
                WorkerDirectory = directoryPath
            };
            IHarnessOptionsValidate harnessOptionsValidate = new HarnessOptionsValidate(stubLogger);

            // Act
            bool actual = harnessOptionsValidate.Validate(options);

            // Assert
            Assert.IsFalse(actual);
        }
    }
}
