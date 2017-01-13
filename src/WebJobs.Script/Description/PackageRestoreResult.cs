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
        /// Returns true if this restore process was the initial package installation (there were no packages installed); otherwise, false.
        /// </summary>
        public bool IsInitialInstall { get; set; }

        /// <summary>
        /// True if the references changed as a result of a restore; otherwise, false.
        /// </summary>
        public bool ReferencesChanged { get; set; }
    }
}
