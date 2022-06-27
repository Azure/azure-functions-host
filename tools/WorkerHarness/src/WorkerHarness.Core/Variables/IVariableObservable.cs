// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core
{
    /// <summary>
    /// interface that will allow an Expression object to subscribe to variable-addition events
    /// and notify all subscribed Expression objects when a new variable has been added
    /// </summary>
    public interface IVariableObservable
    {
        // allow an Expression to subscribe
        void Subscribe(Expression expression);

        // add a new variable and notify all subscribed expressions
        void AddVariable(string variableName, object variableValue);

        // reset the state by removing all variables
        void Clear();
    }
}
