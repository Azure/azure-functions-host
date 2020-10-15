// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.MemStore.MemoryMappedFileHandler.ResponseTypes
{
    /// <inheritdoc/>
    public class ResponseByteArray : ResponseBase
    {
        public ResponseByteArray(bool status, byte[] value)
        {
            this.Success = status;
            this.Value = value;
        }

        public static ResponseByteArray FailureResponse
        {
            get
            {
                return new ResponseByteArray(false, null);
            }
        }

        public byte[] Value { get; private set; }
    }
}
