// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class KeyOperationResult
    {
        public KeyOperationResult(string resultSecret, OperationResult result)
        {
            Secret = resultSecret;
            Result = result;
        }

        public string Secret { get; set; }

        public OperationResult Result { get; set; }
    }
}
