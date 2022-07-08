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
    }
}
