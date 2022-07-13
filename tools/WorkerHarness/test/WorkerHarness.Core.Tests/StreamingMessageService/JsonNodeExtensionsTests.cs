// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Nodes;
using WorkerHarness.Core.StreamingMessageService;
using WorkerHarness.Core.Tests.Helpers;
using WorkerHarness.Core.Variables;

namespace WorkerHarness.Core.Tests.StreamingMessageService
{
    [TestClass]
    public class JsonNodeExtensionsTests
    {
        [TestMethod]
        public void SolveVariables_EmptyNode_ReturnNode()
        {
            // Arrange
            var stubNode = new JsonObject();
            IVariableObservable stubGlobalVariables = new VariableManager();

            // Act
            var actual = stubNode.SolveVariables(stubGlobalVariables);

            // Assert
            Assert.IsTrue(actual is JsonObject);
            Assert.AreEqual(0, ((JsonObject)actual).Count);
        }

        [TestMethod]
        public void SolveVariables_NodeIsJsonValueThatContainsObjectVariable_ReturnNewJsonValue()
        {
            // Arrange
            JsonNode stubNode = JsonValue.Create<string>("${weatherForecast}.Location.State")!;

            object variableValue = WeatherForecast.CreateWeatherForecastObject();
            string variableName = "weatherForecast";
            IVariableObservable globalVariables = new VariableManager();
            globalVariables.AddVariable(variableName, variableValue);

            // Act
            JsonNode newNode = stubNode.SolveVariables(globalVariables);

            // Assert
            Assert.IsTrue(newNode is JsonValue);
            Assert.AreEqual("WA", newNode.GetValue<string>());
        }

        [TestMethod]
        public void SolveVariables_NodeIsJsonValueThatContainsStringVariable_ReturnNewJsonValue()
        {
            // Arrange
            JsonNode stubNode = JsonValue.Create<string>("@{weatherForecast}")!;

            object variableValue = "sunny";
            string variableName = "weatherForecast";
            IVariableObservable globalVariables = new VariableManager();
            globalVariables.AddVariable(variableName, variableValue);

            // Act
            JsonNode newNode = stubNode.SolveVariables(globalVariables);

            // Assert
            Assert.IsTrue(newNode is JsonValue);
            Assert.AreEqual("sunny", newNode.GetValue<string>());
        }

        [TestMethod]
        public void SolveVariables_NodeIsJsonValueThatContainsUnrecognizedVariable_ThrowArgumentException()
        {
            // Arrange
            string expression = "@{unknown_variable}";
            JsonNode stubNode = JsonValue.Create<string>(expression)!;

            object variableValue = "known_value";
            string variableName = "known_variable";
            IVariableObservable globalVariables = new VariableManager();
            globalVariables.AddVariable(variableName, variableValue);

            // Act
            try
            {
                JsonNode newNode = stubNode.SolveVariables(globalVariables);
            }
            // Assert
            catch (ArgumentException ex)
            {
                string expectedMessage = string.Format(JsonNodeExtensions.VariableCannotBeSolved, expression);
                StringAssert.Contains(ex.Message, expectedMessage);
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} is not thrown");
        }

        [TestMethod]
        public void SolveVariables_NodeIsJsonArrayThatContainsNoVariables_ReturnNewArrayWithSameContent()
        {
            // Arrange
            string item1 = "hello";
            string item2 = "world";
            JsonNode stubNode = new JsonArray(item1, item2);

            IVariableObservable globalVariables = new VariableManager();

            // Act
            JsonNode newNode = stubNode.SolveVariables(globalVariables);

            // Assert
            Assert.IsTrue(newNode is JsonArray);
            Assert.IsTrue(((JsonArray)newNode).Count == 2);
            Assert.AreEqual(((JsonArray)newNode)[0]!.GetValue<string>(), item1);
            Assert.AreEqual(((JsonArray)newNode)[1]!.GetValue<string>(), item2);
        }

        [TestMethod]
        public void SolveVariables_NodeIsJsonArrayThatContainsVariables_ReturnNewArray()
        {
            // Arrange
            string item1 = "@{my_cat}";
            string item2 = "${weatherforecast}.Location.City";
            JsonNode stubNode = new JsonArray(item1, item2);

            IVariableObservable globalVariables = new VariableManager();
            globalVariables.AddVariable("weatherforecast", WeatherForecast.CreateWeatherForecastObject());
            globalVariables.AddVariable("my_cat", "Summer");

            // Act
            JsonNode newNode = stubNode.SolveVariables(globalVariables);

            // Assert
            Assert.IsTrue(newNode is JsonArray);
            Assert.IsTrue(((JsonArray)newNode).Count == 2);
            Assert.AreEqual(((JsonArray)newNode)[0]!.GetValue<string>(), "Summer");
            Assert.AreEqual(((JsonArray)newNode)[1]!.GetValue<string>(), "Redmond");
        }

        [TestMethod]
        public void SolveVariables_NodeIsJsonArrayThatContainsUnrecognizedVariable_ThrowArgumentException()
        {
            // Arrange
            string item1 = "@{my_cat}";
            string item2 = "hello, world";
            JsonNode stubNode = new JsonArray(item1, item2);

            IVariableObservable globalVariables = new VariableManager();

            // Act
            try
            {
                JsonNode newNode = stubNode.SolveVariables(globalVariables);
            }
            catch (ArgumentException ex)
            {
                string expectedMessage = string.Format(JsonNodeExtensions.VariableCannotBeSolved, item1);
                StringAssert.Contains(ex.Message, expectedMessage);
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} is not thrown");
        }

        [TestMethod]
        public void SolveVariables_NodeIsJsonObjectThatContainsVariable_ReturnNewNode()
        {
            // Arrange
            JsonNode stubNode = new JsonObject
            {
                ["Name"] = "Summer",
                ["Breed"] = "@{variable}"
            };

            IVariableObservable globalVariables = new VariableManager();
            globalVariables.AddVariable("variable", "American Shorthair");

            // Act
            JsonNode newNode = stubNode.SolveVariables(globalVariables);

            // Assert
            Assert.IsTrue(newNode is JsonObject);
            Assert.AreEqual(2, ((JsonObject)newNode).Count);
            Assert.AreEqual("Summer", ((JsonObject)newNode)["Name"]!.GetValue<string>());
            Assert.AreEqual("American Shorthair", ((JsonObject)newNode)["Breed"]!.GetValue<string>());
        }

        [TestMethod]
        public void SolveVariables_NodeIsJsonObjectThatContainsUnrecognizedVariable_ThrowArgumentException()
        {
            // Arrange
            JsonNode stubNode = new JsonObject
            {
                ["Name"] = "Summer",
                ["Breed"] = "@{variable}"
            };

            IVariableObservable globalVariables = new VariableManager();

            // Act
            try
            {
                JsonNode newNode = stubNode.SolveVariables(globalVariables);
            }
            // Assert
            catch (ArgumentException ex)
            {
                string expectedMessage = string.Format(JsonNodeExtensions.VariableCannotBeSolved, "@{variable}");
                StringAssert.Contains(ex.Message, expectedMessage);
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} is not thrown");
        }

        [TestMethod]
        public void SolveVariables_NodeContainsJsonObjectAndJsonArray_AllVariablesAreSolved()
        {
            // Arrange
            JsonNode stubNode = new JsonObject
            {
                ["Name"] = "Summer",
                ["Appearance"] = new JsonObject
                {
                    ["Color"] = "@{color}",
                    ["Fur"] = "short"
                },
                ["FavoriteFood"] = new JsonArray("@{food}", "chicken"),
                ["Address"] = new JsonObject
                {
                    ["City"] = "${object}.Location.City",
                    ["State"] = "${object}.Location.State"
                }
            };

            IVariableObservable globalVariables = new VariableManager();
            globalVariables.AddVariable("color", "orange");
            globalVariables.AddVariable("food", "turkey");
            globalVariables.AddVariable("object", WeatherForecast.CreateWeatherForecastObject());

            // Act
            JsonNode newNode = stubNode.SolveVariables(globalVariables);

            // Assert
            Assert.IsTrue(newNode is JsonObject);
            Assert.AreEqual("orange", newNode["Appearance"]!["Color"]!.GetValue<string>());
            Assert.AreEqual("turkey", newNode["FavoriteFood"]![0]!.GetValue<string>());
            Assert.AreEqual("Redmond", newNode["Address"]!["City"]!.GetValue<string>());
            Assert.AreEqual("WA", newNode["Address"]!["State"]!.GetValue<string>());
        }

        [TestMethod]
        public void SolveVariables_NodeContainsMissingVariable_ThrowArgumentException()
        {
            // Arrange
            JsonNode stubNode = new JsonObject
            {
                ["Name"] = "Summer",
                ["Appearance"] = new JsonObject
                {
                    ["Color"] = "@{color}",
                    ["Fur"] = "short"
                },
                ["FavoriteFood"] = new JsonArray("@{food}", "chicken"),
                ["Address"] = new JsonObject
                {
                    ["City"] = "${missing_variable}.Location.City",
                }
            };

            IVariableObservable globalVariables = new VariableManager();
            globalVariables.AddVariable("color", "orange");
            globalVariables.AddVariable("food", "turkey");

            // Act
            try
            {
                JsonNode newNode = stubNode.SolveVariables(globalVariables);
            }
            // Assert
            catch (ArgumentException ex)
            {
                string exceptionMessage = string.Format(JsonNodeExtensions.VariableCannotBeSolved, "${missing_variable}.Location.City");
                StringAssert.Contains(ex.Message, exceptionMessage);
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} is not thrown");
        }
    }
}
