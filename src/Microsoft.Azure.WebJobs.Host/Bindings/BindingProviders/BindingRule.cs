// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    /// <summary>
    /// Describes a binding rule. See <see cref="IBindingRuleProvider"/>.
    /// </summary>
    internal class BindingRule
    {
        public static readonly BindingRule[] Empty = new BindingRule[0];
                
        /// <summary>
        /// Gets or sets the binding rule filter.
        /// </summary>
        public string Filter { get; set; }

        /// <summary>
        /// Gets or sets the source attribute type.
        /// </summary>
        public Type SourceAttribute { get; set; }

        /// <summary>
        /// Gets or sets the intermediate converters used to
        /// get to the <see cref="UserType"/>.
        /// </summary>
        public Type[] Converters { get; set; } 

        /// <summary>
        /// Gets or sets the user type this rule can bind to.
        /// </summary>
        public OpenType UserType { get; set; }
    }
}
