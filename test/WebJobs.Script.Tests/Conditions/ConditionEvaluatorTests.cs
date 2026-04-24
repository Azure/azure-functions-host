// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Conditions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Conditions
{
    public class ConditionEvaluatorTests
    {
        [Fact]
        public void EvaluateAll_NullList_ReturnsTrue()
        {
            Assert.True(ConditionEvaluator.EvaluateAll(null));
        }

        [Fact]
        public void EvaluateAll_EmptyList_ReturnsTrue()
        {
            Assert.True(ConditionEvaluator.EvaluateAll(new List<ICondition>()));
        }

        [Fact]
        public void EvaluateAll_AllTrue_ReturnsTrue()
        {
            var conditions = new List<ICondition>
            {
                new StubCondition(true),
                new StubCondition(true),
                new StubCondition(true)
            };

            Assert.True(ConditionEvaluator.EvaluateAll(conditions));
        }

        [Fact]
        public void EvaluateAll_AnyFalse_ReturnsFalse()
        {
            var conditions = new List<ICondition>
            {
                new StubCondition(true),
                new StubCondition(false),
                new StubCondition(true)
            };

            Assert.False(ConditionEvaluator.EvaluateAll(conditions));
        }

        [Fact]
        public void EvaluateAll_ShortCircuitsOnFirstFalse()
        {
            var third = new StubCondition(true);
            var conditions = new List<ICondition>
            {
                new StubCondition(true),
                new StubCondition(false),
                third
            };

            Assert.False(ConditionEvaluator.EvaluateAll(conditions));
            Assert.False(third.Evaluated);
        }

        private sealed class StubCondition : ICondition
        {
            private readonly bool _result;

            public StubCondition(bool result)
            {
                _result = result;
            }

            public bool Evaluated { get; private set; }

            public bool Evaluate()
            {
                Evaluated = true;
                return _result;
            }
        }
    }
}
