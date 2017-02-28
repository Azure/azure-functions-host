// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Enumeration of binding cardinality values.
    /// </summary>
    public enum Cardinality
    {
        /// <summary>
        /// Bind to a single element.
        /// </summary>
        One,

        /// <summary>
        /// Bind to many elements.
        /// </summary>
        Many
    }
}
