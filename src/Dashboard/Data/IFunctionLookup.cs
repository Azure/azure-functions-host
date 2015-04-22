// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Dashboard.Data
{
    internal interface IFunctionLookup
    {
        FunctionSnapshot Read(string functionId);
    }
}
