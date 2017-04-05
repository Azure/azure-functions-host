// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script
{
    public enum FileLoggingMode
    {
        /// <summary>
        /// Never log to file (the default).
        /// </summary>
        Never,

        /// <summary>
        /// Always log to file.
        /// </summary>
        Always,

        /// <summary>
        /// Only log to file when in debug mode.
        /// </summary>
        DebugOnly
    }
}
