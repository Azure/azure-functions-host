// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
