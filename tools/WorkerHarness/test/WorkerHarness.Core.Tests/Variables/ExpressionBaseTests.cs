// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Variables;
using WorkerHarness.Core.Tests.Helpers;

namespace WorkerHarness.Core.Tests.Variables
{
    [TestClass]
    public class ExpressionBaseTests
    {
        [TestMethod]
        [DataRow("${dog}.breed.name", false, 1)]
        [DataRow("@{dog_name}", false, 1)]
        [DataRow("${dog}.@{property}.name", false, 2)]
        [DataRow("", true, 0)]
        [DataRow("I.miss.my.pets", true, 0)]
        public void SetExpression_ValidExpression_AllPropertiesSet(string expression, bool resolved, int dependencyCount)
        {
            // Arrange
            ExpressionBase concreteExpression = new ConcreteExpression();

            // Act
            concreteExpression.SetExpression(expression);

            // Assert
            Assert.AreEqual(expression, concreteExpression.Expression);
            Assert.AreEqual(resolved, concreteExpression.Resolved);
            Assert.AreEqual(dependencyCount, concreteExpression.Dependencies.Count);
        }

        [TestMethod]
        [DataRow("${dog}.fights.${cat}")]
        [DataRow("a.${b}.c")]
        [DataRow("${@{hello}}.a")]
        [DataRow("@{${${world}}}.b")]
        public void SetExpression_InvalidExpression_ThrowArgumentException(string expression)
        {
            // Arrange
            ExpressionBase concreteExpression = new ConcreteExpression();

            // Act
            try
            {
                concreteExpression.SetExpression(expression);
            }
            // Assert
            catch (ArgumentException ex)
            {
                Assert.IsTrue(ex.Message.Contains("Invalid expression"));
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} is not thrown");
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("hello")]
        public void TryResolve_NoVariableExpression_ReturnTrue(string expression)
        {
            // Arrange
            ExpressionBase concreteExpression = new ConcreteExpression();
            concreteExpression.SetExpression(expression);

            // Act
            bool actual = concreteExpression.TryResolve("a", "b");

            // Assert
            Assert.IsTrue(actual);
            Assert.AreEqual(expression, concreteExpression.Expression);
        }

        [TestMethod]
        [DataRow("${cat}.fur.color")]
        [DataRow("Bobby.@{attribute}.public")]
        public void TryResolve_VariableExpressionDoesNotContainInputVariable_ReturnFalse(string expression)
        {
            // Arrange
            ExpressionBase concreteExpression = new ConcreteExpression();
            concreteExpression.SetExpression(expression);

            string variableName = "dog";
            object variableValue = "beagle";

            // Act
            bool actual = concreteExpression.TryResolve(variableName, variableValue);

            // Assert
            Assert.IsFalse(actual);
            Assert.AreEqual(expression, concreteExpression.Expression);
        }

        [TestMethod]
        [DataRow("@{attribute}", "fur", true, 0)]
        [DataRow("${dog}.@{attribute}.color", "${dog}.fur.color", false, 1)]
        [DataRow("${dog}.@{attribute}.@{attribute}", "${dog}.fur.fur", false, 1)]
        public void TryResolve_VariableExpressionContainsInputStringVariable_StringVariableResolved(string expression, string expectedExpression, bool resolved, int dependencyCount)
        {
            // Arrange
            string variableName = "attribute";
            string variableValue = "fur";

            ExpressionBase concreteExpression = new ConcreteExpression();
            concreteExpression.SetExpression(expression);

            // Act
            bool actual = concreteExpression.TryResolve(variableName, variableValue);

            // Assert
            Assert.AreEqual(resolved, actual);
            Assert.AreEqual(resolved, concreteExpression.Resolved);
            Assert.AreEqual(expectedExpression, concreteExpression.Expression);
            Assert.AreEqual(dependencyCount, concreteExpression.Dependencies.Count);
        }

        [TestMethod]
        public void TryResolve_VariableExpressionContainsInputObjectVariable_ObjectVariableResolved()
        {
            // Arrange
            string expression = "${weatherForecast}.Location.ZipCode";
            string expectedExpression = "98052";

            string variableName = "weatherForecast";
            object variableValue = WeatherForecast.CreateWeatherForecastObject();

            ExpressionBase concreteExpression = new ConcreteExpression();
            concreteExpression.SetExpression(expression);

            // Act
            bool resolved = concreteExpression.TryResolve(variableName, variableValue);

            // Assert
            Assert.IsTrue(resolved);
            Assert.IsTrue(concreteExpression.Resolved);
            Assert.AreEqual(expectedExpression, concreteExpression.Expression);
            Assert.AreEqual(0, concreteExpression.Dependencies.Count);
        }

        [TestMethod]
        public void TryResolve_InputStringVariableThenInputObjectVariable_ReturnResolved()
        {
            // Arrange
            string expression = "${weatherForecast}.@{attribute}.ZipCode";
            string expectedExpression = "98052";

            string objectVariableName = "weatherForecast";
            object objectVariableValue = WeatherForecast.CreateWeatherForecastObject();

            string stringVariableName = "attribute";
            object stringVariableValue = "Location";

            ExpressionBase concreteExpression = new ConcreteExpression();
            concreteExpression.SetExpression(expression);

            // Act
            bool firstResult = concreteExpression.TryResolve(stringVariableName, stringVariableValue);
            bool secondResult = concreteExpression.TryResolve(objectVariableName, objectVariableValue);

            // Assert
            Assert.IsFalse(firstResult);
            Assert.IsTrue(secondResult);
            Assert.IsTrue(concreteExpression.Resolved);
            Assert.AreEqual(expectedExpression, concreteExpression.Expression);
            Assert.AreEqual(0, concreteExpression.Dependencies.Count);
        }

        [TestMethod]
        public void TryResolve_InputObjectVariableThenInputStringVariable_ReturnResolved()
        {
            // Arrange
            string expression = "${weatherForecast}.@{attribute}.ZipCode";
            string expectedExpression = "98052";

            string objectVariableName = "weatherForecast";
            object objectVariableValue = WeatherForecast.CreateWeatherForecastObject();

            string stringVariableName = "attribute";
            object stringVariableValue = "Location";

            ExpressionBase concreteExpression = new ConcreteExpression();
            concreteExpression.SetExpression(expression);

            // Act
            bool firstResult = concreteExpression.TryResolve(objectVariableName, objectVariableValue);
            bool secondResult = concreteExpression.TryResolve(stringVariableName, stringVariableValue);

            // Assert
            Assert.IsFalse(firstResult);
            Assert.IsTrue(secondResult);
            Assert.IsTrue(concreteExpression.Resolved);
            Assert.AreEqual(expectedExpression, concreteExpression.Expression);
            Assert.AreEqual(0, concreteExpression.Dependencies.Count);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("hello")]
        [DataRow("Season.Summer.Sports")]
        public void TryEvaluate_ResolvedExpression_ReturnTrue(string expression)
        {
            // Arrange
            ExpressionBase concreteExpression = new ConcreteExpression();
            concreteExpression.SetExpression(expression);

            // Act
            bool actual = concreteExpression.TryEvaluate(out string? evaluatedExpression);

            // Assert
            Assert.IsTrue(actual);
            Assert.AreEqual(expression, evaluatedExpression);
        }

        [TestMethod]
        [DataRow("${dog}.butt.color")]
        [DataRow("@{a}")]
        [DataRow("${dog}.@{attribute}.color")]
        public void TryEvaluate_UnresolvedExpression_ReturnFalse(string expression)
        {
            // Arrange
            ExpressionBase concreteExpression = new ConcreteExpression();
            concreteExpression.SetExpression(expression);

            // Act
            bool actual = concreteExpression.TryEvaluate(out string? evaluatedExpression);

            // Assert
            Assert.IsFalse(actual);
            Assert.IsNull(evaluatedExpression);
        }

        private class ConcreteExpression : ExpressionBase
        {
            public override void ConstructExpression()
            {
                throw new NotImplementedException();
            }
        }
    }

}
