// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    /// <summary>
    /// Represents the input values for a triggered function invocation.
    /// </summary>
    /// <typeparam name="TTriggerValue">The trigger value Type</typeparam>
    public class TriggeredFunctionData<TTriggerValue> : TriggeredFunctionData
    {
        /// <summary>
        /// The trigger value for a specific triggered function invocation.
        /// </summary>
        public new TTriggerValue TriggerValue { get; set; }
    }
}
