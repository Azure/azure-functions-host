// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.MemStore.MemoryMappedFileHandler.ResponseTypes
{
    /// <inheritdoc/>
    public class ResponseLong : ResponseBase
    {
        public ResponseLong(bool status, long value)
        {
            this.Success = status;
            this.Value = value;
        }

        public static ResponseLong FailureResponse
        {
            get
            {
                return new ResponseLong(false, -1);
            }
        }

        public long Value { get; private set; }
    }
}
