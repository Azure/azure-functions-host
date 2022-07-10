// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Tests.Helpers;
using WorkerHarness.Core.Validators;

namespace WorkerHarness.Core.Tests.Validators
{
    [TestClass]
    public class StringValidatorTests
    {
        [TestMethod]
        [DataRow("${object}.A.B")]
        [DataRow("hello")]
        [DataRow("$.NonexistingProperty")]
        public void Validate_QueryThrowsExceptions_ThrowArgumentException(string query)
        {
            // Arrange
            ValidationContext validationContext = new()
            {
                Query = query
            };
            validationContext.ConstructExpression();

            object stubObject = WeatherForecast.CreateWeatherForecastObject();

            StringValidator stringValidator = new();

            // Act
            try
            {
                stringValidator.Validate(validationContext, stubObject);
            }
            // Assert
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, StringValidator.ValidationExceptionMessage);
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} exception is not thrown");
        }

        [TestMethod]
        public void Validate_QueryResultFailsToMatchExpected_ReturnFalse()
        {
            // Arrange
            ValidationContext context = new()
            {
                Query = "$.Location.State",
                Expected = "MA"
            };
            context.ConstructExpression();

            object stubObject = WeatherForecast.CreateWeatherForecastObject();

            StringValidator stringValidator = new();

            // Act
            bool actual = stringValidator.Validate(context, stubObject);

            // Assert
            Assert.IsFalse(actual);
        }

        [TestMethod]
        public void Validate_QueryResultMatchesExpected_ReturnTrue()
        {
            // Arrange
            ValidationContext context = new()
            {
                Query = "$.Location.State",
                Expected = "WA"
            };
            context.ConstructExpression();

            object stubObject = WeatherForecast.CreateWeatherForecastObject();

            StringValidator stringValidator = new();

            // Act
            bool actual = stringValidator.Validate(context, stubObject);

            // Assert
            Assert.IsTrue(actual);
        }
    }
}
