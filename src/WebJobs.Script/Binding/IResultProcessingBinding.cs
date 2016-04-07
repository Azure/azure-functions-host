// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    /// <summary>
    /// Represents a binding that may process function execution results.
    /// </summary>
    public interface IResultProcessingBinding
    {
        void ProcessResult(IDictionary<string, object> functionArguments, object[] systemArguments, string triggerInputName, object result);

        bool CanProcessResult(object result);
    }
}
