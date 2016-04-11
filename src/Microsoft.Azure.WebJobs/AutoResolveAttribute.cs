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
    }
}