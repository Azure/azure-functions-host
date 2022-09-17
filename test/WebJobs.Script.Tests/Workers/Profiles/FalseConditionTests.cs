// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Profiles
{
    public class FalseConditionTests
    {
        [Fact]
        public void FalseCondition_EvaluatesToFalse()
        {
            var falseCondition = new FalseCondition();
            Assert.False(falseCondition.Evaluate(), "False condition must always return false");
        }
    }
}