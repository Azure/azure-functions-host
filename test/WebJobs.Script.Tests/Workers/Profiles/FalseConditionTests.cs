// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Workers.Profiles
{
    public class FalseConditionTests
    {
        [Fact]
        public void FalseCondition_EvaluesFalse()
        {
            var falseCondition = new FalseCondition();
            Assert.False(falseCondition.Evaluate(), "False condition must always return false");
        }
    }
}