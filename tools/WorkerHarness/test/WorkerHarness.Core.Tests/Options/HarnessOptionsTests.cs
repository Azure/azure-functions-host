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
                WorkerExecutable = stubFiles[1],
                WorkerDirectory = directoryPath
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
                WorkerExecutable = stubFiles[1],
                WorkerDirectory = directoryPath
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
                WorkerExecutable = stubFiles[1],
                WorkerDirectory = directoryPath
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
                WorkerExecutable = stubFiles[1],
                WorkerDirectory = directoryPath
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
        public void Validate_HarnessOptionsWithValidWorkerExecutable_ReturnTrue(string relativeWorkerExecutable)
        {
            // Arrange
            string directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "ScenarioFileSamples");
            string[] stubFiles = Directory.GetFiles(directoryPath);

            HarnessOptions options = new()
            {
                ScenarioFile = stubFiles[0],
                LanguageExecutable = stubFiles[1],
                WorkerExecutable = relativeWorkerExecutable,
                WorkerDirectory = directoryPath
            };
            IHarnessOptionsValidate harnessOptionsValidate = new HarnessOptionsValidate(stubLogger);

            string expected = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relativeWorkerExecutable));

            // Act
            bool actual = harnessOptionsValidate.Validate(options);

            // Assert
            Assert.IsTrue(actual);
            Assert.IsTrue(Path.IsPathRooted(options.WorkerExecutable));
            Assert.AreEqual(expected, options.WorkerExecutable);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow(@"ScenarioFileSamples\NotExistingScenario.json")]
        public void Validate_HarnessOptionsWithInvalidWorkerExecutable_ReturnFalse(string relativeWorkerExecutable)
        {
            // Arrange
            string directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "ScenarioFileSamples");
            string[] stubFiles = Directory.GetFiles(directoryPath);

            HarnessOptions options = new()
            {
                ScenarioFile = stubFiles[0],
                LanguageExecutable = stubFiles[1],
                WorkerExecutable = relativeWorkerExecutable,
                WorkerDirectory = directoryPath
            };
            IHarnessOptionsValidate harnessOptionsValidate = new HarnessOptionsValidate(stubLogger);

            // Act
            bool actual = harnessOptionsValidate.Validate(options);

            // Assert
            Assert.IsFalse(actual);
        }

        [TestMethod]
        [DataRow(@"ScenarioFileSamples")]
        [DataRow(@"ScenarioFileSamples")]
        [DataRow(@"..\net6.0\ScenarioFileSamples")]
        public void Validate_HarnessOptionsWithValidWorkerDirectory_ReturnTrue(string relativeWorkerDirectory)
        {
            // Arrange
            string directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "ScenarioFileSamples");
            string[] stubFiles = Directory.GetFiles(directoryPath);

            HarnessOptions options = new()
            {
                ScenarioFile = stubFiles[0],
                LanguageExecutable = stubFiles[1],
                WorkerExecutable = stubFiles[2],
                WorkerDirectory = relativeWorkerDirectory
            };
            IHarnessOptionsValidate harnessOptionsValidate = new HarnessOptionsValidate(stubLogger);

            string expected = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relativeWorkerDirectory));

            // Act
            bool actual = harnessOptionsValidate.Validate(options);

            // Assert
            Assert.IsTrue(actual);
            Assert.IsTrue(Path.IsPathRooted(options.WorkerDirectory));
            Assert.AreEqual(expected, options.WorkerDirectory);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow(@"NonExistentScenarioFileSamples")]
        public void Validate_HarnessOptionsWithInvalidWorkerDirectory_ReturnFalse(string relativeWorkerDirectory)
        {
            // Arrange
            string directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "ScenarioFileSamples");
            string[] stubFiles = Directory.GetFiles(directoryPath);

            HarnessOptions options = new()
            {
                ScenarioFile = stubFiles[0],
                LanguageExecutable = stubFiles[1],
                WorkerExecutable = stubFiles[2],
                WorkerDirectory = relativeWorkerDirectory
            };
            IHarnessOptionsValidate harnessOptionsValidate = new HarnessOptionsValidate(stubLogger);

            // Act
            bool actual = harnessOptionsValidate.Validate(options);

            // Assert
            Assert.IsFalse(actual);
        }
    }
}
