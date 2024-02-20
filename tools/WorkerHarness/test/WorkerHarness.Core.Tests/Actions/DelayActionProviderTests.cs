// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using WorkerHarness.Core.Actions;

namespace WorkerHarness.Core.Tests.Actions
{
    [TestClass]
    public class DelayActionProviderTests
    {
        [TestMethod]
        public void Create_ActionNodeHasNoDelayProperty_ReturnDelayAction()
        {
            // Arrange
            DelayActionProvider provider = new(new LoggerFactory());

            JsonNode actionNode = new JsonObject
            {
                ["actionType"] = "delay"
            };

            // Act
            IAction action = provider.Create(actionNode);

            // Assert
            Assert.IsTrue(action is DelayAction);
            Assert.AreEqual(ActionTypes.Delay, provider.Type);
            Assert.AreEqual(0, ((DelayAction) action).DelayInMilliseconds);
        }

        [TestMethod]
        public void Create_ActionNodeHasDelayProperty_ReturnDelayAction()
        {
            // Arrange
            DelayActionProvider provider = new(new LoggerFactory());

            int milisecondsDelay = 5000;
            JsonNode actionNode = new JsonObject
            {
                ["actionType"] = "delay",
                ["delay"] = milisecondsDelay
            };

            // Act
            IAction action = provider.Create(actionNode);

            // Assert
            Assert.IsTrue(action is DelayAction);
            Assert.AreEqual(ActionTypes.Delay, provider.Type);
            Assert.AreEqual(milisecondsDelay, ((DelayAction)action).DelayInMilliseconds);
        }
    }
}
