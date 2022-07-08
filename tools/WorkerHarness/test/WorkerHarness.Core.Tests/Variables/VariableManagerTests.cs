// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Variables;
using Moq;

namespace WorkerHarness.Core.Tests.Variables
{
    [TestClass]
    public class VariableManagerTests
    {
        [TestMethod]
        public void Subscribe_ExpressionHasVariable_VariablesDictioanryIsEmpty_ExpressionsListIncreaseCountByOne()
        {
            // Arrange
            IDictionary<string, object> variables = new Dictionary<string, object>();
            IList<IExpression> expressions = new List<IExpression>();
            IVariableObservable variableManager = new VariableManager(variables, expressions);

            string? evaluatedExpression = null;
            var mockExpression = new Mock<IExpression>();

            mockExpression
                .SetupSequence(x => x.TryEvaluate(out evaluatedExpression))
                .Returns(false)
                .Returns(false);

            int presubscribedCount = expressions.Count;

            // Act
            variableManager.Subscribe(mockExpression.Object);

            // Assert
            Assert.AreEqual(presubscribedCount + 1, expressions.Count);
            Assert.IsTrue(expressions.Contains(mockExpression.Object));
        }

        [TestMethod]
        public void Subscribe_ExpressionHasNoVariable_VariablesDictionaryIsEmpty_ExpressionsListDoesNotChange()
        {
            // Arrange
            IDictionary<string, object> variables = new Dictionary<string, object>();
            IList<IExpression> expressions = new List<IExpression>();
            IVariableObservable variableManager = new VariableManager(variables, expressions);

            string? evaluatedExpression = "hello, world";
            var mockExpression = new Mock<IExpression>();

            mockExpression
                .Setup(x => x.TryEvaluate(out evaluatedExpression))
                .Returns(true);

            int presubscribedCount = expressions.Count;

            // Act
            variableManager.Subscribe(mockExpression.Object);

            // Assert
            Assert.AreEqual(presubscribedCount, expressions.Count);
            Assert.IsFalse(expressions.Contains(mockExpression.Object));
        }

        [TestMethod]
        public void Subscribe_ExpressionHasVariable_VariablesDictionaryCannotResolveExpression_ExpressionsListIncreaseCount()
        {
            // Arrange
            IDictionary<string, object> variables = new Dictionary<string, object>()
            {
                { "object1", new object() },
                { "object2", new object() },
                { "object3", new object() }
            };
            IList<IExpression> expressions = new List<IExpression>();
            IVariableObservable variableManager = new VariableManager(variables, expressions);

            string? evaluatedExpression = "hello, world";
            var mockExpression = new Mock<IExpression>();

            mockExpression
                .SetupSequence(x => x.TryEvaluate(out evaluatedExpression))
                .Returns(false)
                .Returns(false);

            mockExpression.SetupSequence(x => x.TryResolve(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(false)
                .Returns(false)
                .Returns(true);

            int presubscribedCount = expressions.Count;

            // Act
            variableManager.Subscribe(mockExpression.Object);

            // Assert
            Assert.AreEqual(presubscribedCount + 1, expressions.Count);
            Assert.IsTrue(expressions.Contains(mockExpression.Object));
        }

        [TestMethod]
        public void Subscribe_ExpressionHasVariable_VariablesDictionaryResolvesExpression_ExpressionsListDoesNotChange()
        {
            // Arrange
            IDictionary<string, object> variables = new Dictionary<string, object>()
            {
                { "object1", new object() },
                { "object2", new object() },
                { "object3", new object() }
            };
            IList<IExpression> expressions = new List<IExpression>();
            IVariableObservable variableManager = new VariableManager(variables, expressions);

            string? evaluatedExpression = "hello, world";
            var mockExpression = new Mock<IExpression>();

            mockExpression
                .SetupSequence(x => x.TryEvaluate(out evaluatedExpression))
                .Returns(false)
                .Returns(true);

            mockExpression.SetupSequence(x => x.TryResolve(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(false)
                .Returns(false)
                .Returns(true);

            int presubscribedCount = expressions.Count;

            // Act
            variableManager.Subscribe(mockExpression.Object);

            // Assert
            Assert.AreEqual(presubscribedCount, expressions.Count);
            Assert.IsFalse(expressions.Contains(mockExpression.Object));
        }

        [TestMethod]
        public void AddVariable_AddTheSameVariable_ThrowInvalidDataException()
        {
            // Arrange
            IDictionary<string, object> variables = new Dictionary<string, object>()
            {
                { "object1", new object() },
                { "object2", new object() },
                { "object3", new object() }
            };
            IList<IExpression> expressions = new List<IExpression>();
            IVariableObservable variableManager = new VariableManager(variables, expressions);

            // Act
            try
            {
                variableManager.AddVariable("object1", new object());
            }
            // Assert
            catch (InvalidDataException ex)
            {
                Assert.AreEqual(ex.Message, string.Format(VariableManager.DuplicateVariableMessage, "object1"));
                return;
            }

            Assert.Fail($"The expected {typeof(InvalidDataException)} exception is not thrown");
        }

        [TestMethod]
        public void AddVariable_AnExpressionIsResolved_ExpressionsListCountDecreasesByOne()
        {
            // Arrange
            IDictionary<string, object> variables = new Dictionary<string, object>();
            IList<IExpression> expressions = new List<IExpression>();
            IVariableObservable variableManager = new VariableManager(variables, expressions);

            var mockExpression1 = new Mock<IExpression>();
            mockExpression1
                .Setup(x => x.TryResolve(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(false);
            expressions.Add(mockExpression1.Object);

            var mockExpression2 = new Mock<IExpression>();
            mockExpression2
                .Setup(x => x.TryResolve(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(true);
            expressions.Add(mockExpression2.Object);

            int expectedCount = expressions.Count - 1;

            // Act
            variableManager.AddVariable("objectName", new object());
            
            // Assert
            Assert.AreEqual(expectedCount, expressions.Count);
            Assert.IsTrue(expressions.Contains(mockExpression1.Object));
            Assert.IsFalse(expressions.Contains(mockExpression2.Object));
        }

        [TestMethod]
        public void AddVariable_NoExpressionIsResolved_ExpressionsListCountDoesNotChange()
        {
            // Arrange
            IDictionary<string, object> variables = new Dictionary<string, object>();
            IList<IExpression> expressions = new List<IExpression>();
            IVariableObservable variableManager = new VariableManager(variables, expressions);

            var mockExpression1 = new Mock<IExpression>();
            mockExpression1
                .Setup(x => x.TryResolve(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(false);
            expressions.Add(mockExpression1.Object);

            var mockExpression2 = new Mock<IExpression>();
            mockExpression2
                .Setup(x => x.TryResolve(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(false);
            expressions.Add(mockExpression2.Object);

            int expectedCount = expressions.Count;

            // Act
            variableManager.AddVariable("objectName", new object());

            // Assert
            Assert.AreEqual(expectedCount, expressions.Count);
            Assert.IsTrue(expressions.Contains(mockExpression1.Object));
            Assert.IsTrue(expressions.Contains(mockExpression2.Object));
        }

        [TestMethod]
        public void Clear_ClearEmptyState_EmptyState()
        {
            // Arrange
            IDictionary<string, object> variables = new Dictionary<string, object>();
            IList<IExpression> expressions = new List<IExpression>();
            IVariableObservable variableManager = new VariableManager(variables, expressions);

            // Act
            variableManager.Clear();

            // Assert
            Assert.AreEqual(0, expressions.Count);
            Assert.AreEqual(0, variables.Count);
        }

        [TestMethod]
        public void Clear_ClearNonemptyState_EmptyState()
        {
            // Arrange
            IDictionary<string, object> variables = new Dictionary<string, object>()
            {
                { "object1", new object() },
                { "object2", new object() }
            };


            IList<IExpression> expressions = new List<IExpression>
            {
                new Mock<IExpression>().Object,
                new Mock<IExpression>().Object
            };

            IVariableObservable variableManager = new VariableManager(variables, expressions);

            // Act
            variableManager.Clear();

            // Assert
            Assert.AreEqual(0, expressions.Count);
            Assert.AreEqual(0, variables.Count);
        }
    }
}
