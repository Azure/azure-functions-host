// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;

namespace Microsoft.Azure.WebJobs.Script.Extensibility
{
    /// <summary>
    /// Class providing the metadata information needed by the script runtime to interact
    /// with an extension binding.
    /// </summary>
    public abstract class ScriptBinding
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ScriptBinding"/> class.
        /// </summary>
        /// <param name="context">The binding context.</param>
        protected ScriptBinding(ScriptBindingContext context)
        {
            Context = context;
        }

        /// <summary>
        /// Gets the <see cref="ScriptBindingContext"/> for this binding.
        /// </summary>
        public ScriptBindingContext Context { get; private set; }

        /// <summary>
        /// Gets the default <see cref="Type"/> that this binding should
        /// use to bind.
        /// </summary>
        public abstract Type DefaultType { get; }

        /// <summary>
        /// Gets the collection of <see cref="Attribute"/>s that should
        /// be applied to the binding.
        /// </summary>
        /// <returns>The collection of attributes.</returns>
        public abstract Collection<Attribute> GetAttributes();
    }
}
