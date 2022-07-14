// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
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
            object value = obj.Query(query);

            // Assert
            Assert.IsTrue(value is string);
            string valueInString = (string)value;
            Assert.AreEqual("Redmond", valueInString);
        }

        [TestMethod]
        public void Query_ValidQueryThatHasArrayIndexing_ReturnString()
        {
            // Arrange
            object obj = WeatherForecast.CreateWeatherForecastObject();
            string query = "$.Summary[0]";

            string expected = ((WeatherForecast)obj).Summary[0];

            // Act
            object value = obj.Query(query);

            // Assert
            Assert.IsTrue(value is string);
            string valueInString = (string)value;
            Assert.AreEqual(expected, valueInString);
        }

        [TestMethod]
        public void Query_RootQuery_ReturnObject()
        {
            // Arrange
            object obj = WeatherForecast.CreateWeatherForecastObject();
            string query = "$.";

            // Act
            object value = obj.Query(query);

            // Assert
            Assert.AreEqual(JsonSerializer.Serialize(obj), JsonSerializer.Serialize(value));
        }

        [TestMethod]
        public void Query_ValidQuery_ReturnObject()
        {
            // Arrange
            object obj = WeatherForecast.CreateWeatherForecastObject();
            string query = "$.Location";

            // Act
            object value = obj.Query(query);

            // Assert
            string expected = JsonSerializer.Serialize(((WeatherForecast)obj).Location);
            string actual = JsonSerializer.Serialize(value);
            Assert.AreEqual(expected, actual); 
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
