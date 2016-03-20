// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Host.Bindings.Runtime
{
    /// <summary>
    /// Context object for <see cref="IBinderEx"/> used to specify
    /// binding details.
    /// </summary>
    public class RuntimeBindingContext
    {
        private ReadOnlyCollection<Attribute> _additionalAttributes;

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="attribute">The attribute to bind.</param>
        /// <param name="additionalAttributes">Additional attributes to apply to the binding.</param>
        public RuntimeBindingContext(Attribute attribute, Attribute[] additionalAttributes = null)
        {
            Attribute = attribute;

            if (additionalAttributes != null)
            {
                _additionalAttributes = new ReadOnlyCollection<Attribute>(additionalAttributes.ToList());
            }
            else
            {
                _additionalAttributes = new ReadOnlyCollection<Attribute>(new List<Attribute>());
            }
        }

        /// <summary>
        /// Gets the WebJobs SDK Attribute to bind to (e.g. <see cref="QueueAttribute"/>, etc.)
        /// </summary>
        public Attribute Attribute { get; private set; }

        /// <summary>
        /// Gets the additional parameter level attributes to apply to the binding.
        /// </summary>
        public IReadOnlyCollection<Attribute> AdditionalAttributes
        {
            get
            {
                return _additionalAttributes;
            }
        }
    }
}
