// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    /// <summary>
    /// An implementation of an <see cref="IWorkerProfileCondition"/> that always evaluates to false.
    /// This condition is used to disable a profile when condition providers fail to resolve conditions.
    /// </summary>
    public class FalseCondition : IWorkerProfileCondition
    {
        public bool Evaluate()
        {
            return false;
        }
    }
}