// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Validators;

namespace WorkerHarness.Core.Tests.Validators
{
    [TestClass]
    public class ValidationContextTests
    {
        [TestMethod]
        public void Constructor_DefaultToStringType()
        {
            // Arrage + Act
            ValidationContext validationContext = new();

            // Assert
            Assert.IsTrue(string.Equals(validationContext.Type, "string", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        [DataRow("${dog}.${cat}.A")]
        [DataRow("dog.${cat}.A")]
        [DataRow("${@{animal_type}}.A")]
        public void ConstructExpression_ExpectedPropertyIsInvalidExpression_ThrowArgumentException(string expected)
        {
            // Arrange
            ValidationContext validationContext = new()
            {
                Expected = expected
            };

            // Act
            try
            {
                validationContext.ConstructExpression();
            }
            // Assert
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, "Failed to construct an expression from the expected value");
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} exception is not thrown");
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("hello")]
        [DataRow("${dog}.@{attribute}.color")]
        public void ConstructExpression_ExpectedPropertyIsValidExpression_ExpressionPropertyMatchesExpectedProperty(string expected)
        {
            // Arrange
            ValidationContext validationContext = new()
            {
                Expected = expected
            };

            // Act
            validationContext.ConstructExpression();

            // Assert
            Assert.AreEqual(validationContext.Expected, validationContext.Expression);
        }
    }
}
