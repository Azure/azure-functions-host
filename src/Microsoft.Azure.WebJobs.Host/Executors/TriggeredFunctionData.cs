// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    /// <summary>
    /// Represents the input values for a triggered function invocation.
    /// </summary>
    public class TriggeredFunctionData
    {
        /// <summary>
        /// The parent ID for the triggered function invocation.
        /// </summary>
        public Guid? ParentId { get; set; }

        /// <summary>
        /// The trigger value for a specific triggered function invocation.
        /// </summary>
        public object TriggerValue { get; set; }
    }
}
