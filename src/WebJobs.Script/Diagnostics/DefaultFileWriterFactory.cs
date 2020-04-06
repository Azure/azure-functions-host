// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script
{
    internal class DefaultFileWriterFactory : IFileWriterFactory
    {
        /// <summary>
        /// Creates an IFileWriter. The caller is responsible for disposing of the IFileWriter.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        /// <returns>An IFileWriter.</returns>
        public IFileWriter Create(string filePath) => new FileWriter(filePath);
    }
}
