// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script
{
    public interface IScriptHostEnvironment
    {
        /// <summary>
        /// Restarts the <see cref="ScriptHost"/>.
        /// </summary>
        void RestartHost();

        /// <summary>
        /// Stops the <see cref="ScriptHost"/> and shuts down the hosting environment.
        /// </summary>
        void Shutdown();
    }
}
