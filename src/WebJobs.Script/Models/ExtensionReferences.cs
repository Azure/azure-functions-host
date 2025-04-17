// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Models
{
    /// <summary>
    /// Represents a collection of extension references.
    /// </summary>
    public sealed class ExtensionReferences
    {
        public ExtensionReference[] Extensions { get; init; } = [];
    }
}
