// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Allows consumers to perform operations against the Job Host environment.
    /// </summary>
    public interface IScriptJobHostEnvironment
    {
        /// <summary>
        /// Restarts the <see cref="IScriptJobHost"/>.
        /// </summary>
        void RestartHost();

        /// <summary>
        /// Stops the <see cref="IScriptJobHost"/> and shuts down the hosting environment.
        /// </summary>
        void Shutdown();
    }
}
