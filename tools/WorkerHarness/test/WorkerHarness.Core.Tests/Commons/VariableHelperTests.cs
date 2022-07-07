// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using WorkerHarness.Core.Commons;

namespace WorkerHarness.Core.Tests.Commons
{
    [TestClass]
    public class VariableHelperTests
    {
        [TestMethod]
        public void ValidateVariableExpression_ExpressionContainsManyObjectVariables_ThrowInvalidDataException()
        {
            // Arrange
            string expression = "${}.${}.${a}.@{}.@{b}";
            string expectedExceptionMessage = string.Format(VariableHelper.MoreThanOneObjectVariableMessage, 3, expression);

            // Act
            try
            {
                VariableHelper.ValidateVariableExpression(expression);
            }
            // Assert
            catch (InvalidDataException ex)
            {
                Assert.AreEqual(ex.Message, expectedExceptionMessage);
                return;
            }

            Assert.Fail($"The expected {typeof(InvalidDataException)} is not thrown");
        }

        [TestMethod]
        public void ValidateVariableExpression_ExpressionContainsOneObjectVariableNotAtBeginning_ThrowInvalidDataException()
        {
            // Arrange
            string expression = "@{a}.${c}.b";
            string expectedExceptionMessage = string.Format(VariableHelper.ObjectVariableNotAtZeroIndexMessage, "${c}.", 5);

            // Act
            try
            {
                VariableHelper.ValidateVariableExpression(expression);
            }
            // Assert
            catch (InvalidDataException ex)
            {
                Assert.AreEqual(ex.Message, expectedExceptionMessage);
                return;
            }

            Assert.Fail($"The expected {typeof(InvalidDataException)} is not thrown");
        }

        [TestMethod]
        public void ValidateVariableExpression_ExpressionContainsEmptyNestedVariables_ThrowInvalidDataException()
        {
            // Arrange
            string expression = "${@{}}.A.B";
            string expectedExceptionMessage = string.Format(VariableHelper.NestedVariableMessage, 1, "${@{}}.", expression);

            // Act
            try
            {
                VariableHelper.ValidateVariableExpression(expression);
            }
            // Assert
            catch (InvalidDataException ex)
            {
                Assert.AreEqual(ex.Message, expectedExceptionMessage);
                return;
            }

            Assert.Fail($"The expected {typeof(InvalidDataException)} is not thrown");
        }

        [TestMethod]
        public void ValidateVariableExpression_ExpressionContainsNonemptyNestedVariables_ThrowInvalidDataException()
        {
            // Arrange
            string expression = "${@{hello}}.A.B";
            string expectedExceptionMessage = string.Format(VariableHelper.NestedVariableMessage, 1, "${@{hello}}.", expression);

            // Act
            try
            {
                VariableHelper.ValidateVariableExpression(expression);
            }
            // Assert
            catch (InvalidDataException ex)
            {
                Assert.AreEqual(ex.Message, expectedExceptionMessage);
                return;
            }

            Assert.Fail($"The expected {typeof(InvalidDataException)} is not thrown");
        }

        [TestMethod]
        public void ContainVariables_ValidExpressionContainsOnlyObjectVariables_ReturnTrue()
        {
            // Arrange
            string expression = "${hello}.A.B";

            // Act
            bool actual = VariableHelper.ContainVariables(expression);

            // Assert
            Assert.IsTrue(actual);
        }

        [TestMethod]
        public void ContainVariables_ValidExpressionContainsOnlyStringVariable_ReturnTrue()
        {
            // Arrange
            string expression = "@{hello}";

            // Act
            bool actual = VariableHelper.ContainVariables(expression);

            // Assert
            Assert.IsTrue(actual);
        }

        [TestMethod]
        public void ContainVariables_ValidExpressionContainsBothObjectAndStringVariables_ReturnTrue()
        {
            // Arrange
            string expression = "${A}.B.@{C}";

            // Act
            bool actual = VariableHelper.ContainVariables(expression);

            // Assert
            Assert.IsTrue(actual);
        }

        [TestMethod]
        public void ContainVariables_ValidExpressionNotContainVariables_ReturnFalse()
        {
            // Arrange
            string expression = "ABC";

            // Act
            bool actual = VariableHelper.ContainVariables(expression);

            // Assert
            Assert.IsFalse(actual);
        }

        [TestMethod]
        public void ExtractVariableNames_ValidExpressionWithStringAndObjectVariables_ReturnListOfString()
        {
            // Arrange
            string expression = "${corgi}.@{beagle}.AmericanShortHair.@{frenchies}";
            IList<string> expected = new List<string>() { "corgi", "beagle", "frenchies" };

            // Act
            IList<string> actual = VariableHelper.ExtractVariableNames(expression);

            // Assert
            Assert.IsTrue(actual.Any());
            foreach (string doggie in expected)
            {
                Assert.IsTrue(actual.Contains(doggie));
            }
        }

        [TestMethod]
        public void ExtractVariableNames_ValidExpressionContainsNoVariables_ReturnEmptyListOfString()
        {
            // Arrange
            string expression = "BengalCat.BritishShortHair.AmericanShortHair";

            // Act
            IList<string> actual = VariableHelper.ExtractVariableNames(expression);

            // Assert
            Assert.IsTrue(actual.Count == 0);
        }

        [TestMethod]
        public void ResolveStringVariable_ExpressionWithOneStringVariable_ReturnString()
        {
            // Arrange
            string expression = "${corgi}.hair.@{color}";
            string variableName = "color";
            string variableValue = "brown";
            string expected = "${corgi}.hair.brown";

            // Act
            string actual = VariableHelper.ResolveStringVariable(variableName, variableValue, expression);

            // Assert
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void ResolveStringVariable_ExpressionWithSameStringVariable_ReturnString()
        {
            // Arrange
            string expression = "${corgi}.hair.@{color}.butt.@{color}";
            string variableName = "color";
            string variableValue = "brown";
            string expected = "${corgi}.hair.brown.butt.brown";

            // Act
            string actual = VariableHelper.ResolveStringVariable(variableName, variableValue, expression);

            // Assert
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void ResolveStringVariable_ExpressionWithDifferentStringVariables_ReturnString()
        {
            // Arrange
            string expression = "${corgi}.butt.@{butt_color}.feet.@{feet_color}";
            string variableName = "feet_color";
            string variableValue = "white";
            string expected = "${corgi}.butt.@{butt_color}.feet.white";

            // Act
            string actual = VariableHelper.ResolveStringVariable(variableName, variableValue, expression);

            // Assert
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void ResolveStringVariable_ExpressionWithNoMatchingVariableName_ReturnExpression()
        {
            // Arrange
            string expression = "${corgi}.butt.@{butt_color}.feet.@{feet_color}";
            string variableName = "eye_color";
            string variableValue = "black";

            // Act
            string actual = VariableHelper.ResolveStringVariable(variableName, variableValue, expression);

            // Assert
            Assert.AreEqual(expression, actual);
        }

        [TestMethod]
        public void ResolveObjectVariable_ExpressionThatHasVariable_ReturnString()
        {
            // Arrange
            string variableName = "WeatherForecast";
            object variableValue = CreateWeatherForecastObject();
            string expression = "${WeatherForecast}.Location.State";
            string expected = "WA";

            // Act
            string actual = VariableHelper.ResolveObjectVariable(variableName, variableValue, expression);

            // Assert
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void ResolveObjectVariable_ExpressionThatHasVariable_ReturnSerializedString()
        {
            // Arrange
            string variableName = "WeatherForecast";
            object variableValue = CreateWeatherForecastObject();
            string expression = "${WeatherForecast}.Location";
            string expected = JsonSerializer.Serialize(((WeatherForecast)variableValue).Location);

            // Act
            string actual = VariableHelper.ResolveObjectVariable(variableName, variableValue, expression);

            // Assert
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void  ResolveObjectVariable_ExpressionThatDoesNotHaveVariable_ReturnSameExpression()
        {
            // Arrange
            string variableName = "MoodPredictor";
            object variableValue = CreateWeatherForecastObject();
            string expression = "${WeatherForecast}.Location";
            string expected = expression;

            // Act
            string actual = VariableHelper.ResolveObjectVariable(variableName, variableValue, expression);

            // Assert
            Assert.AreEqual(expected, actual);
        }

        private static object CreateWeatherForecastObject()
        {
            var obj = new WeatherForecast()
            {
                Location = new Location()
                {
                    City = "Redmond",
                    State = "WA",
                    ZipCode = "98052"
                },
                TemperatureInFahrenheit = 73,
                Summary = "Cloudy, Rainy"
            };

            return obj;
        }

        private class WeatherForecast
        {
            public Location? Location { get; set; }
            public int TemperatureInFahrenheit { get; set; }
            public string? Summary { get; set; }
        }

        private class Location
        {
            public string? City { get; set; }
            public string? State { get; set; }
            public string? ZipCode { get; set; }
        }


    }
}
