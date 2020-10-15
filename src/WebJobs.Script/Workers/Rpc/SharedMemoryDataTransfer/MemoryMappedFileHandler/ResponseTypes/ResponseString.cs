// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.MemStore.MemoryMappedFileHandler.ResponseTypes
{
    /// <inheritdoc/>
    public class ResponseString : ResponseBase
    {
        public ResponseString(bool status, string value)
        {
            this.Success = status;
            this.Value = value;
        }

        public static ResponseString FailureResponse
        {
            get
            {
                return new ResponseString(false, null);
            }
        }

        public string Value { get; private set; }
    }
}
