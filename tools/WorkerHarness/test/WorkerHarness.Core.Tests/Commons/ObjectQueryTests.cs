// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Commons;

namespace WorkerHarness.Core.Tests.Commons
{
    [TestClass]
    public class ObjectQueryTests
    {
        [TestMethod]
        public void Query_ValidQueryParameter_ReturnString()
        {
            // Arrange
            object obj = CreateObject();
            string query = "$.Location.City";

            // Act
            string value = obj.Query(query);

            // Assert
            Assert.AreEqual("Redmond", value);
        }

        [TestMethod]
        public void Query_QueryContainsNoDolarSign_ThrowArgumentException()
        {
            // Arrange
            object obj = CreateObject();
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
            object obj = CreateObject();
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
            object obj = CreateObject();
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

        private static object CreateObject()
        {
            var obj = new WeatherForcast()
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

        private class WeatherForcast
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
