// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;

namespace Microsoft.MemStore.MemoryMappedFileHandler.ResponseTypes
{
    /// <inheritdoc/>
    public class ResponseStream : ResponseBase
    {
        public ResponseStream(bool status, Stream value)
        {
            this.Success = status;
            this.Value = value;
        }

        public static ResponseStream FailureResponse
        {
            get
            {
                return new ResponseStream(false, null);
            }
        }

        public Stream Value { get; private set; }
    }
}
