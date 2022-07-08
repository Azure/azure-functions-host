// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Matching;

namespace WorkerHarness.Core.Tests.Matching
{
    [TestClass]
    public class MatchingContextTests
    {
        [TestMethod]
        public void Constructor_DefaultToStringType()
        {
            // Arrage + Act
            MatchingContext matchingContext = new();

            // Assert
            Assert.AreEqual("string", matchingContext.Type);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("hello")]
        [DataRow("${dog}.@{attribute}.color")]
        public void ConstructExpression_ExpressionPropertyMatchesExpectedProperty(string expected)
        {
            // Arrange
            MatchingContext matchingContext = new()
            {
                Expected = expected
            };

            // Act
            matchingContext.ConstructExpression();

            // Assert
            Assert.AreEqual(expected, matchingContext.Expression);
        }

        [TestMethod]
        [DataRow("${dog}.${cat}.A")]
        [DataRow("dog.${cat}.A")]
        [DataRow("dog.${cat}.A")]
        [DataRow("${@{animal_type}}.A")]
        public void ConstructExpression_ExpectedIsInvalidExpression_ThrowArgumentException(string expected)
        {
            // Arrange
            MatchingContext matchingContext = new()
            {
                Expected = expected
            };

            // Act
            try
            {
                matchingContext.ConstructExpression();
            }
            //Assert
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, "Failed to construct an expression from the expected value");
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} exception is not thrown");
        }
    }
}
