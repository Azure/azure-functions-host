// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE_APACHE.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Http.Features
{
    public interface IScriptHttpBufferedStream
    {
        Task DisableBufferingAsync(CancellationToken cancellationToken);
    }
}
