// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class DelegatingStream : Stream
    {
        private readonly Stream _inner;

        public DelegatingStream(Stream inner)
        {
            _inner = inner;
        }

        protected Stream Inner { get { return _inner; }}

        public override bool CanRead
        {
            get { return _inner.CanRead; }
        }

        public override bool CanSeek
        {
            get { return _inner.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return _inner.CanWrite; }
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        public override void Close()
        {
            _inner.Close();
        }

        public override long Length
        {
            get { return _inner.Length; }
        }

        public override long Position
        {
            get
            {
                return _inner.Position;
            }
            set
            {
                _inner.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
        }

        public override void WriteByte(byte value)
        {
            _inner.WriteByte(value);
        }

        public override int GetHashCode()
        {
            return _inner.GetHashCode();
        }
    }
}
