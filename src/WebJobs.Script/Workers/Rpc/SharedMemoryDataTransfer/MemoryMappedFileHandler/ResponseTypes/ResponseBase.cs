// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.MemStore.MemoryMappedFileHandler.ResponseTypes
{
    /// <summary>
    /// Class used to return some object(s) along with a status indicating success or failure.
    /// This is used primarily in async functions' return types since they don't support "out"
    /// parameters to return object(s) while at the same time indicating success or failure.
    /// </summary>
    public abstract class ResponseBase
    {
        public bool Success { get; protected set; }
    }
}
