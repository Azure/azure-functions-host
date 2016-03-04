// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    /// <summary>
    /// Represents a binding that may process function execution results.
    /// </summary>
    public interface IResultProcessingBinding
    {
        void ProcessResult(object inputValue, object result);

        bool CanProcessResult(object result);
    }
}
