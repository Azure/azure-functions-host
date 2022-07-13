// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Moq;
using System.Text.Json.Nodes;
using WorkerHarness.Core.Commons;
using WorkerHarness.Core.Tests.Helpers;
using WorkerHarness.Core.Variables;

namespace WorkerHarness.Core.Tests.Commons
{
    [TestClass]
    public class JsonNodeExtensions
    {
        [TestMethod]
        public void SolveVariables_EmptyNode_ReturnNode()
        {
            // Arrange
            var stubNode = new JsonObject();
            IVariableObservable stubGlobalVariables = new Mock<IVariableObservable>().Object;

            // Act
            var actual = stubNode.SolveVariables(stubGlobalVariables);

            // Assert
            Assert.AreEqual(stubNode, actual);
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
    }
}
