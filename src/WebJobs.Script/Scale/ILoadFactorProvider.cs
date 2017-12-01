// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Scale
{
    public interface ILoadFactorProvider
    {
        /// <summary>
        /// Returns a number from 0 to 1 indicating the current level
        /// of load. 0 means no work is in progress or remains to do, and
        /// 1 means completely overloaded.
        /// </summary>
        /// <returns>The load factor.</returns>
        double GetLoadFactor();
    }
}
