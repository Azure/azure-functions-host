// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Description
{
    /// <summary>
    /// Place this on an attribute to note that it's a binding attribute. 
    /// An extension should then claim this and bind it. 
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BindingAttribute : Attribute
    {
    }
}
