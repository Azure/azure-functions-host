// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Place this on binding attributes properties to tell the binders that that the property
    /// should be automatically resolved as an app setting
    /// </summary>
    [Obsolete("Not ready for public consumption.")]
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class AppSettingAttribute : Attribute
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public AppSettingAttribute()
        {
        }

        /// <summary>
        /// The default app setting name to use, if none specified
        /// </summary>
        public string Default { get; set; }
    }
}