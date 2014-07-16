// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Dashboard.Data
{
    internal interface IFunctionInstanceLogger
    {
        void LogFunctionStarted(FunctionInstanceSnapshot snapshot);

        void LogFunctionCompleted(FunctionInstanceSnapshot snapshot);
    }
}
