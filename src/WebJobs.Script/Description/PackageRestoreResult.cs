// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class PackageRestoreResult
    {
        /// <summary>
        /// Gets or sets a value indicating whether the restore process was the initial package installation (there were no packages installed).
        /// </summary>
        public bool IsInitialInstall { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether references have changed as a result of a restore.
        /// </summary>
        public bool ReferencesChanged { get; set; }
    }
}
