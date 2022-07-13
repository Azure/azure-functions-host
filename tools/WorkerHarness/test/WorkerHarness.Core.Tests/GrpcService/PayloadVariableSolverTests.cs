// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Nodes;
using WorkerHarness.Core.GrpcService;
using WorkerHarness.Core.Tests.Helpers;
using WorkerHarness.Core.Variables;

namespace WorkerHarness.Core.Tests.GrpcService
{
    [TestClass]
    public class PayloadVariableSolverTests
    {
        [TestMethod]
        public void SolveVariables_PayloadSolveVariablesThrowsArgumentException_ReturnFalse()
        {
            // Arrange
            JsonNode payload = new JsonObject
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

            PayloadVariableSolver payloadVariableSolver = new();

            // Act
            bool actual = payloadVariableSolver.SolveVariables(out JsonNode newPayload, payload, globalVariables);

            // Assert
            Assert.IsFalse(actual);
            Assert.IsTrue(newPayload is JsonObject);
            Assert.IsTrue(((JsonObject)newPayload).Count == 0);
        }

        [TestMethod]
        public void SolveVariables_PayloadSolveVariablesSucceeds_ReturnTrue()
        {
            // Arrange
            JsonNode payload = new JsonObject
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

            PayloadVariableSolver payloadVariableSolver = new();

            // Act
            bool actual = payloadVariableSolver.SolveVariables(out JsonNode newPayload, payload, globalVariables);

            // Assert
            Assert.IsTrue(actual);
            Assert.IsTrue(newPayload is JsonObject);
            Assert.AreEqual("orange", newPayload["Appearance"]!["Color"]!.GetValue<string>());
            Assert.AreEqual("turkey", newPayload["FavoriteFood"]![0]!.GetValue<string>());
            Assert.AreEqual("Redmond", newPayload["Address"]!["City"]!.GetValue<string>());
            Assert.AreEqual("WA", newPayload["Address"]!["State"]!.GetValue<string>());
        }
    }
}
