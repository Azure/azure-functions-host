// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Place this on binding attributes properties to tell the binders that that the property contains
    /// {key} or "%setting%" segments that should be automatically resolved.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class AutoResolveAttribute : Attribute
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public AutoResolveAttribute()
        {
            AllowTokens = true;
        }

        /// <summary>
        /// If true, the property allows tokens. Any value within %% will be automatically resolved.
        /// If false, the property does not allow tokens. The property value itself is automatically resolved.
        /// Default value is true.
        /// </summary>
        public bool AllowTokens { get; set; }

        /// <summary>
        /// Specifies a type to use for runtime binding resolution. That type must derive from IResolutionPolicy, found
        /// in the Microsoft.Azure.WebJobs.Host assembly.
        /// </summary>
        public Type ResolutionPolicyType { get; set; }
    }
}