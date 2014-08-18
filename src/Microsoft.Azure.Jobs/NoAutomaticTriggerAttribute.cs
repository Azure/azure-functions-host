// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Represents an attribute that indicates a function for which no automatic trigger listening is performed.
    /// </summary>
    /// <remarks>
    /// This attribute is useful in two cases:
    /// <list type="number">
    /// <item>
    /// <term>Functions with triggers</term>
    /// <description>Prevents automatic invocation of the triggers, allowing manual polling.</description>
    /// </item>
    /// <item>
    /// <term>Functions without other attributes</term>
    /// <description>Flags the function as an available job function.</description>
    /// </item>
    /// </list>
    /// In both cases, functions marked with this attribute are never called automatically by JobHost (during
    /// RunAndBlock). Instead, they must be invoked manually using the Call method.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method)]
    public class NoAutomaticTriggerAttribute : Attribute
    {
    }
}
