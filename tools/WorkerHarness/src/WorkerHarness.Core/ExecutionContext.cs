// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Variables;

namespace WorkerHarness.Core
{
    public class ExecutionContext
    {
        public IVariableObservable GlobalVariables { get; private set; }

        public ExecutionContext(IVariableObservable variableManager)
        {
            GlobalVariables = variableManager;
        }
    }
}
