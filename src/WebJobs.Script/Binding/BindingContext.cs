// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class BindingContext
    {
        /// <summary>
        /// Gets or sets the collection of Attributes to bind.
        /// </summary>
        public Attribute[] Attributes { get; set; }

        /// <summary>
        /// Gets or sets the binder to use to perform the bind.
        /// </summary>
        public Binder Binder { get; set; }

        /// <summary>
        /// Gets or sets the trigger value that caused the current invocation.
        /// </summary>
        public object TriggerValue { get; set; }

        /// <summary>
        /// Gets or sets the data type hint for the binding.
        /// </summary>
        public DataType DataType { get; set; }

        /// <summary>
        /// Gets or sets the cardinality hint for the binding.
        /// </summary>
        public Cardinality Cardinality { get; set; }

        /// <summary>
        /// Gets or sets the target value the binding is binding to.
        /// </summary>
        public object Value { get; set; }

        /// <summary>
        /// Gets or sets the collection of binding data for this invocation.
        /// </summary>
        public IReadOnlyDictionary<string, object> BindingData { get; set; }
    }
}
