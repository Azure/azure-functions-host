// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class BindingContext
    {
        /// <summary>
        /// Gets or sets the binder to use to perform the bind.
        /// </summary>
        public IBinderEx Binder { get; set; }

        /// <summary>
        /// Gets or sets the trigger value that caused the current invocation.
        /// </summary>
        public object TriggerValue { get; set; }

        /// <summary>
        /// Gets or sets the data type hint for the binding.
        /// </summary>
        public DataType DataType { get; set; }

        /// <summary>
        /// Gets or sets the target value the binding is binding to.
        /// </summary>
        public object Value { get; set; }
        
        /// <summary>
        /// Gets or sets the collection of binding data for this invocation.
        /// </summary>
        public IReadOnlyDictionary<string, string> BindingData { get; set; }
    }
}
