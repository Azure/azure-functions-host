// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Commons;
using WorkerHarness.Core.Tests.Helpers;

namespace WorkerHarness.Core.Tests.Commons
{
    [TestClass]
    public class ObjectQueryTests
    {
        [TestMethod]
        public void Query_ValidQueryParameter_ReturnString()
        {
            // Arrange
            object obj = WeatherForecast.CreateWeatherForecastObject();
            string query = "$.Location.City";

            // Act
            string value = obj.Query(query);

            // Assert
            Assert.AreEqual("Redmond", value);
        }

        [TestMethod]
        public void Query_ValidQueryThatHasArrayIndexing_ReturnString()
        {
            // Arrange
            object obj = WeatherForecast.CreateWeatherForecastObject();
            string query = "$.Summary[0]";

            string expected = ((WeatherForecast)obj).Summary[0];

            // Act
            string value = obj.Query(query);

            // Assert
            Assert.AreEqual(expected, value);
        }

        [TestMethod]
        public void Query_QueryContainsNoDolarSign_ThrowArgumentException()
        {
            // Arrange
            object obj = WeatherForecast.CreateWeatherForecastObject();
            string invalidQuery = "Location.City";

            // Act
            try
            {
                obj.Query(invalidQuery);
            }
            // Assert
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, string.Format(ObjectQuery.InvalidQueryMessage, invalidQuery));
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} exception was not thrown");
        }

        [TestMethod]
        public void Query_QueryContainsDollarSignAndBrackets_ThrowArgumentException()
        {
            // Arrange
            object obj = WeatherForecast.CreateWeatherForecastObject();
            string invalidQuery = "${obj}.Location.City";

            // Act
            try
            {
                obj.Query(invalidQuery);
            }
            // Assert
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, string.Format(ObjectQuery.InvalidQueryMessage, invalidQuery));
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} exception was not thrown");
        }

        [TestMethod]
        public void Query_QueryContainsInvalidProperty_ThrowArgumentException()
        {
            // Arrange
            object obj = WeatherForecast.CreateWeatherForecastObject();
            string invalidQuery = "$.Location.Street";

            // Act
            try
            {
                obj.Query(invalidQuery);
            }
            // Assert
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, string.Format(ObjectQuery.MissingPropertyMessage, "Street"));
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} exception was not thrown");
        }
    }
}
