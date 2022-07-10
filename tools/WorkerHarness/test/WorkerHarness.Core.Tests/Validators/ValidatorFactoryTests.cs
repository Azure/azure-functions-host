// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Validators;

namespace WorkerHarness.Core.Tests.Validators
{
    [TestClass]
    public class ValidatorFactoryTests
    {
        [TestMethod]
        [DataRow("")]
        [DataRow("hello")]
        public void Create_InvalidValidatorTypes_ThrowArgumentException(string validatorType)
        {
            // Arrange
            IValidatorFactory validatorFactory = new ValidatorFactory();

            // Act
            try
            {
                validatorFactory.Create(validatorType);
            }
            // Assert
            catch (ArgumentException ex)
            {
                Assert.AreEqual(ex.Message, string.Format(ValidatorFactory.InvalidValidatorTypeMessage, validatorType));
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} exception is not thrown");
        }

        [TestMethod]
        [DataRow("string")]
        [DataRow("String")]
        public void Create_StringValidatorType_ReturnStringValidator(string stringType)
        {
            // Arrange
            IValidatorFactory validatorFactory = new ValidatorFactory();

            // Act
            IValidator validator = validatorFactory.Create(stringType);

            // Assert
            Assert.AreEqual(typeof(StringValidator), validator.GetType());
        }

        [TestMethod]
        [DataRow("regex")]
        [DataRow("Regex")]
        public void Create_RegexValidatorType_ReturnRegexValidator(string regexType)
        {
            // Arrange
            IValidatorFactory validatorFactory = new ValidatorFactory();

            // Act
            IValidator validator = validatorFactory.Create(regexType);

            // Assert
            Assert.AreEqual(typeof(RegexValidator), validator.GetType());
        }
    }
}
