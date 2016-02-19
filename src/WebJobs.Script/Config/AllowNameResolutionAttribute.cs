// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script
{
    // Attribute to mark a string property has embedded %key% and supports INameResolver resolution. 
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class AllowNameResolutionAttribute : Attribute
    {
    }
}