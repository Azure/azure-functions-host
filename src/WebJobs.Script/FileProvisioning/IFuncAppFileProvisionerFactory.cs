// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.FileProvisioning
{
    internal interface IFuncAppFileProvisionerFactory
    {
        /// <summary>
        /// Creates the file augmentor for the given runtime
        /// </summary>
        /// <param name="runtime">The name of the runtime</param>
        /// <returns>Fun app file augmentor <see cref="IFuncAppFileProvisioner"/></returns>
        IFuncAppFileProvisioner CreatFileProvisioner(string runtime);
    }
}
