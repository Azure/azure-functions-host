// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Place this on binding attributes properties to tell the binders that that the property contains
    /// {key} or "%setting%" segments that should be automatically resolved.
    /// </summary>
    [Obsolete("Not ready for public consumption.")]
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class AutoResolveAttribute : Attribute
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public AutoResolveAttribute()
        {
        }

        /// <summary>
        /// Specifies a type to use for runtime binding resolution. That type must derive from IResolutionPolicy, found
        /// in the Microsoft.Azure.WebJobs.Host assembly.
        /// </summary>
        public Type ResolutionPolicyType { get; set; }
    }
}