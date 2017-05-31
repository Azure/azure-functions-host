// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Interface for providing a connection string name.
    /// </summary>
    public interface IConnectionProvider
    {
        /// <summary>
        /// Gets or sets the app setting name that contains the connection string.
        /// </summary>
        string Connection { get; set; }
    }
}
