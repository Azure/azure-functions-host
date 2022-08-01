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
                WorkerPath = stubFiles[2],
            };
            IHarnessOptionsValidate harnessOptionsValidate = new HarnessOptionsValidate(stubLogger);

            // Act
            bool actual = harnessOptionsValidate.Validate(options);

            // Assert
            Assert.IsTrue(actual);
            Assert.IsTrue(Path.IsPathRooted(options.ScenarioFile));
            Assert.IsTrue(Path.IsPathRooted(options.LanguageExecutable));
            Assert.IsTrue(Path.IsPathRooted(options.WorkerPath));
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
        [DataRow(@"ScenarioFileSamples\ValidScenario.json")]
        [DataRow(@"ScenarioFileSamples\NoScenarioName.json")]
        [DataRow(@"..\net6.0\ScenarioFileSamples\ValidScenario.json")]
        public void Validate_HarnessOptionsWithValidScenarioFile_ReturnTrue(string relativeScenarioPath)
        {
            // Arrange
            string directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "ScenarioFileSamples");
            string[] stubFiles = Directory.GetFiles(directoryPath);

            HarnessOptions options = new()
            {
                ScenarioFile = relativeScenarioPath,
                LanguageExecutable = stubFiles[0],
                WorkerPath = stubFiles[1],
            };
            IHarnessOptionsValidate harnessOptionsValidate = new HarnessOptionsValidate(stubLogger);

            string expectedScenarioPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relativeScenarioPath));

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
        public void Validate_HarnessOptionsWithInvalidScenarioFile_ReturnFalse(string relativeScenarioPath)
        {
            // Arrange
            string directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "ScenarioFileSamples");
            string[] stubFiles = Directory.GetFiles(directoryPath);

            HarnessOptions options = new()
            {
                ScenarioFile = relativeScenarioPath,
                LanguageExecutable = stubFiles[0],
                WorkerPath = stubFiles[1],
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
        [DataRow(@"..\net6.0\ScenarioFileSamples\ValidScenario.json")]
        public void Validate_HarnessOptionsWithValidLanguageExecutable_ReturnTrue(string relativeLanguageExecutable)
        {
            // Arrange
            string directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "ScenarioFileSamples");
            string[] stubFiles = Directory.GetFiles(directoryPath);

            HarnessOptions options = new()
            {
                ScenarioFile = stubFiles[0],
                LanguageExecutable = relativeLanguageExecutable,
                WorkerPath = stubFiles[1],
            };
            IHarnessOptionsValidate harnessOptionsValidate = new HarnessOptionsValidate(stubLogger);

            string expected = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relativeLanguageExecutable));

            // Act
            bool actual = harnessOptionsValidate.Validate(options);

            // Assert
            Assert.IsTrue(actual);
            Assert.IsTrue(Path.IsPathRooted(options.LanguageExecutable));
            Assert.AreEqual(expected, options.LanguageExecutable);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow(@"ScenarioFileSamples\NotExistingScenario.json")]
        public void Validate_HarnessOptionsWithInvalidLanguageExecutable_ReturnFalse(string relativeLanguageExecutable)
        {
            // Arrange
            string directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "ScenarioFileSamples");
            string[] stubFiles = Directory.GetFiles(directoryPath);

            HarnessOptions options = new()
            {
                ScenarioFile = stubFiles[0],
                LanguageExecutable = relativeLanguageExecutable,
                WorkerPath = stubFiles[1]
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
        [DataRow(@"..\net6.0\ScenarioFileSamples\ValidScenario.json")]
        public void Validate_HarnessOptionsWithValidWorkerPath_ReturnTrue(string relativeWorkerExecutable)
        {
            // Arrange
            string directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "ScenarioFileSamples");
            string[] stubFiles = Directory.GetFiles(directoryPath);

            HarnessOptions options = new()
            {
                ScenarioFile = stubFiles[0],
                LanguageExecutable = stubFiles[1],
                WorkerPath = relativeWorkerExecutable,
            };
            IHarnessOptionsValidate harnessOptionsValidate = new HarnessOptionsValidate(stubLogger);

            string expected = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relativeWorkerExecutable));

            // Act
            bool actual = harnessOptionsValidate.Validate(options);

            // Assert
            Assert.IsTrue(actual);
            Assert.IsTrue(Path.IsPathRooted(options.WorkerPath));
            Assert.AreEqual(expected, options.WorkerPath);
            Assert.IsTrue(Path.IsPathRooted(options.WorkerDirectory));
            Assert.AreEqual(Path.GetDirectoryName(expected), options.WorkerDirectory);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow(@"ScenarioFileSamples\NotExistingScenario.json")]
        public void Validate_HarnessOptionsWithInvalidWorkerPath_ReturnFalse(string relativeWorkerExecutable)
        {
            // Arrange
            string directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "ScenarioFileSamples");
            string[] stubFiles = Directory.GetFiles(directoryPath);

            HarnessOptions options = new()
            {
                ScenarioFile = stubFiles[0],
                LanguageExecutable = stubFiles[1],
                WorkerPath = relativeWorkerExecutable,
            };
            IHarnessOptionsValidate harnessOptionsValidate = new HarnessOptionsValidate(stubLogger);

            // Act
            bool actual = harnessOptionsValidate.Validate(options);

            // Assert
            Assert.IsFalse(actual);
        }
    }
}
