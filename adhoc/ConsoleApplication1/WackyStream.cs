#if false
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure.StorageClient;

namespace ConsoleApplication1
{
    class BaseStream : Stream
    {
        public override bool CanRead
        {
            get { throw new NotImplementedException(); }
        }

        public override bool CanSeek
        {
            get { throw new NotImplementedException(); }
        }

        public override bool CanWrite
        {
            get { throw new NotImplementedException(); }
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override long Length
        {
            get { throw new NotImplementedException(); }
        }

        public override long Position
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
    
    // Writes to this stream get incrementally published to the blob
    class BlobStreamWriter : BaseStream
    {
        public override bool CanWrite
        {
            get
            {
                return true;
            }
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, count);
        }
    }

    // Write incrementally to the blob.
    // Flush every N bytes, or after at timer. 
    class BlobTextWriter : TextWriter
    {
        CloudBlob _blob;

        public BlobTextWriter()
        {
            
        }

        void TimerFlush()
        {
        }

        public override Encoding Encoding
        {
            get { return Encoding.UTF8;  }
        }

        public override void Write(char value)
        {
            _blob.Up

            Inner.Write(value);
        }
        public override void Write(char[] buffer, int index, int count)
        {
            Inner.Write(buffer, index, count);
        }
    }

    // $$$ Need to Flush on same thread. 
    class TimerTextWriter : TextWriter
    {

    }

    class DelegatingTextWriter : TextWriter
    {
        public TextWriter Inner;

        public override Encoding Encoding
        {
            get { return Inner.Encoding;  }
        }        

        public override void Write(char value)
        {
            Inner.Write(value);
        }
        public override void Write(char[] buffer, int index, int count)
        {
            Inner.Write(buffer, index, count);
        }
    }

    // Write to the source, and that gets published as a read.
    class WackyStream : Stream
    {
        

        class WackyWriter : Stream
        {
            public override bool CanRead
            {
                get { return false;  }
            }

            public override bool CanSeek
            {
                get { return false;  }
            }

            public override bool CanWrite
            {
                get { return true; }
            }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override long Length
            {
                get { throw new NotImplementedException(); }
            }

            public override long Position
            {
                get
                {
                    throw new NotImplementedException();
                }
                set
                {
                    throw new NotImplementedException();
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }
        }

        public override bool CanRead
        {
            get {
                return true;    
            }
        }

        public override bool CanSeek
        {
            get {
                return false; 

            }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override long Length
        {
            get { throw new NotImplementedException(); }
        }

        public override long Position
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        // The total number of bytes read into the buffer. 
        // 0 on end-of-stream
        public override int Read(byte[] buffer, int offset, int count)
        {
            // $$$
            if (count > 0)
            {
                if (_counter > 10)
                {
                    return 0;

                }
                buffer[offset] = (byte) ('A' + _counter);
                
                _counter++;
                return 1;
            }
            return 0;
        }

        int _counter = 0;

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}


#endif