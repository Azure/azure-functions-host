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
            string[] files = Directory.GetFiles(Directory.GetCurrentDirectory());
            HarnessOptions options = new()
            {
                ScenarioFile = files[0],
                LanguageExecutable = files[0],
                WorkerExecutable = files[0],
                WorkerDirectory = Directory.GetCurrentDirectory()
            };
            IHarnessOptionsValidate harnessOptionsValidate = new HarnessOptionsValidate(stubLogger);

            // Act
            bool actual = harnessOptionsValidate.Validate(options);

            // Assert
            Assert.IsTrue(actual);
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
    }
}
