// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO.MemoryMappedFiles;

namespace Microsoft.MemStore.MemoryMappedFileHandler.ResponseTypes
{
    /// <inheritdoc/>
    public class ResponseMemoryMappedFile : ResponseBase
    {
        public ResponseMemoryMappedFile(bool status, MemoryMappedFile value, string mapName)
        {
            this.Success = status;
            this.Value = value;
            this.MapName = mapName;
        }

        public static ResponseMemoryMappedFile FailureResponse
        {
            get
            {
                return new ResponseMemoryMappedFile(false, null, null);
            }
        }

        public MemoryMappedFile Value { get; private set; }

        public string MapName { get; private set; }
    }
}
