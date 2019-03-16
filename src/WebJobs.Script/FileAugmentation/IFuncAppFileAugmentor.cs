// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.FileAugmentation
{
    public interface IFuncAppFileAugmentor
    {
        /// <summary>
        /// Adds the required files to the function app
        /// </summary>
        /// <param name="scriptRootPath">The root path of the function app</param>
        /// <returns>An empty completed <see cref="Task"/> object with</returns>
        Task AugmentFiles(string scriptRootPath);
    }
}
