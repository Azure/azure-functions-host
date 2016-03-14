// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Describe a function instance in the logs. 
    /// </summary>
    public interface IFunctionInstanceBaseEntry
    {
        /// <summary>
        /// Unique instance id specifically for this instance of the function. 
        /// </summary>
        Guid FunctionInstanceId { get; }

        /// <summary>
        /// Name of the function, which can be used in further lookups about other instances of this function.
        /// For example, "Foo".
        /// </summary>
        string FunctionName { get; }

        /// <summary>
        /// Current status of this instance. 
        /// </summary>
        FunctionInstanceStatus Status { get; }
    } 
}