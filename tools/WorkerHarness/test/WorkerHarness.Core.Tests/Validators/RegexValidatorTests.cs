// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Tests.Helpers;
using WorkerHarness.Core.Validators;

namespace WorkerHarness.Core.Tests.Validators
{
    [TestClass]
    public class RegexValidatorTests
    {
        [TestMethod]
        [DataRow("${dog_breed}.origin")]
        [DataRow("A.B.C")]
        [DataRow("$.InvalidProperty")]
        public void Validate_QueryThrow_ThrowArgumentException(string query)
        {
            // Arrange
            ValidationContext context = new()
            {
                Query = query
            };
            context.ConstructExpression();

            object stubbObject = WeatherForecast.CreateWeatherForecastObject();

            RegexValidator regexValidator = new();

            // Act
            try
            {
                regexValidator.Validate(context, stubbObject);
            }
            // Assert
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, RegexValidator.ValidationExceptionMessage);
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} exception is not thrown");
        }

        [TestMethod]
        public void Validate_QueryResultFailsToMatchRegexExpression_ReturnFalse()
        {
            // Arrange
            ValidationContext context = new()
            {
                Query = "$.Location.ZipCode",
                Expected = "^[0-9]{4}$"
            };
            context.ConstructExpression();

            object stubObject = WeatherForecast.CreateWeatherForecastObject();

            RegexValidator regexValidator = new();

            // Act
            bool actual = regexValidator.Validate(context, stubObject);

            // Assert
            Assert.IsFalse(actual);
        }

        [TestMethod]
        public void Validate_QueryResultMatchesRegexExpression_ReturnTrue()
        {
            // Arrange
            ValidationContext context = new()
            {
                Query = "$.Location.ZipCode",
                Expected = "^[0-9]{5}$"
            };
            context.ConstructExpression();

            object stubObject = WeatherForecast.CreateWeatherForecastObject();

            RegexValidator regexValidator = new();

            // Act
            bool actual = regexValidator.Validate(context, stubObject);

            // Assert
            Assert.IsTrue(actual);
        }

        [TestMethod]
        public void Validate_QueryResultIsAnObjectThatMatchesRegexExpression_ReturnTrue()
        {
            // Arrange
            object stubObject = WeatherForecast.CreateWeatherForecastObject();

            string expected = ".+";
            ValidationContext context = new()
            {
                Query = "$.Location",
                Expected = expected
            };
            context.ConstructExpression();

            RegexValidator regexValidator = new();

            // Act
            bool actual = regexValidator.Validate(context, stubObject);

            // Assert
            Assert.IsTrue(actual);
        }
    }
}
