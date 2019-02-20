// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE_APACHE.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;

namespace Microsoft.AspNetCore.Buffering
{
    internal class HttpBufferingFeature : IHttpBufferingFeature, IScriptHttpBufferedStream
    {
        private readonly BufferingWriteStream _buffer;
        private readonly IHttpBufferingFeature _innerFeature;

        internal HttpBufferingFeature(BufferingWriteStream buffer, IHttpBufferingFeature innerFeature)
        {
            _buffer = buffer;
            _innerFeature = innerFeature;
        }

        public Task DisableBufferingAsync(CancellationToken cancellationToken)
        {
            return _buffer.DisableBufferingAsync(cancellationToken);
        }

        public void DisableRequestBuffering()
        {
            _innerFeature?.DisableRequestBuffering();
        }

        public void DisableResponseBuffering()
        {
            _buffer.DisableBuffering();
            _innerFeature?.DisableResponseBuffering();
        }
    }
}
