// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Event arguments for the <see cref="HostNameProvider.HostNameChanged"/> event.
    /// </summary>
    public sealed class HostNameChangedEventArgs : EventArgs
    {
        public HostNameChangedEventArgs(string previousHostName, string newHostName)
        {
            PreviousHostName = previousHostName;
            NewHostName = newHostName;
        }

        /// <summary>
        /// Gets the previous hostname before the change.
        /// </summary>
        public string PreviousHostName { get; }

        /// <summary>
        /// Gets the new hostname after the change.
        /// </summary>
        public string NewHostName { get; }
    }
}
