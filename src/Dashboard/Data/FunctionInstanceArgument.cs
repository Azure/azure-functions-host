// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Dashboard.Data
{
    public class FunctionInstanceArgument
    {
        public string Value { get; set; }

        public bool IsBlob { get; set; }

        public bool IsBlobOutput { get; set; }

        public string AccountName { get; set; }
    }
}
