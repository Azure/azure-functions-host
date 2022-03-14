// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal interface IWorkerProfileConditionFactory
    {
        IWorkerProfileCondition CreateWorkerProfileCondition(string type, string name, string expression);
    }
}
