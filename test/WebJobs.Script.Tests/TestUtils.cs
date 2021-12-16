// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestUtils
    {
        /// <summary>
        /// Get a <see cref="byte[]"/> filled with random bytes equal to the specified count.
        /// Note:
        /// <see cref="byte[]"/> can hold maximum of 2GB content.
        /// </summary>
        /// <param name="numBytes">Number of bytes to fill in the output array.</param>
        /// <returns><see cref="byte[]"/> with random bytes.</returns>
        public static byte[] GetRandomBytesInArray(int numBytes)
        {
            byte[] buffer = new byte[numBytes];
            Random random = new Random();
            random.NextBytes(buffer);
            return buffer;
        }

        /// <summary>
        /// Get a random string filled with alpha-numeric characters.
        /// </summary>
        /// <param name="length">Number of characters in the output string.</param>
        /// <returns><see cref="string"/> with random characters.</returns>
        public static string GetRandomString(int length)
        {
            Random random = new Random();
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            IEnumerable<char> randomChars = Enumerable.Repeat(chars, length).Select(s => s[random.Next(s.Length)]);
            char[] randomCharsArray = randomChars.ToArray();
            return new string(randomCharsArray);
        }

        /// <summary>
        /// Get a <see cref="Stream"/> containing random content equal to the number of bytes specified.
        /// Note:
        /// This method uses <see cref="UnmanagedMemoryStream"/> to support writing more than 2GB of data which
        /// is not supported by <see cref="MemoryStream"/>.
        /// The caller must allocate memory equal to the number of bytes required and must free that memory after use.
        /// </summary>
        /// <param name="size">Number of bytes to write into the stream.</param>
        /// <param name="memIntPtr">Pointer to allocated memory from which to create the stream.</param>
        /// <returns><see cref="Stream"/> containing random bytes.</returns>
        public static unsafe Stream GetRandomContentInStream(long size, IntPtr memIntPtr)
        {
            // Get a byte pointer from the IntPtr object
            byte* memBytePtr = (byte*)memIntPtr.ToPointer();

            // Create an UnmanagedStream from the allocated memory
            UnmanagedMemoryStream stream = new UnmanagedMemoryStream(memBytePtr, size);

            // byte[] can't be of more than 2GB; using 1GB buffers to write
            const int maxBufferCapacity = 1 * 1024 * 1024 * 1024;

            // Write into the UnmanagedStream in chunks of maxBufferCapacity
            long remaining = size;
            while (remaining > 0)
            {
                // Safe to cast to int because it cannot be more than maxBufferCapacity which is an int
                int toWrite = (int)Math.Min(remaining, maxBufferCapacity);
                byte[] bytes = GetRandomBytesInArray(toWrite);
                remaining -= bytes.Length;
                stream.WriteAsync(bytes, 0, bytes.Length);
            }

            stream.Flush();
            stream.Seek(0, SeekOrigin.Begin);

            return stream;
        }

        /// <summary>
        /// Compares two streams to check if their contents are equal.
        /// </summary>
        /// <param name="stream1">First <see cref="Stream"/> to compare.</param>
        /// <param name="stream2">Second <see cref="Stream"/> to compare.</param>
        /// <returns><see cref="true"/> if contents of both streams are equal, <see cref="false"/> otherwise.</returns>
        public static async Task<bool> StreamEqualsAsync(Stream stream1, Stream stream2)
        {
            if (stream1 == stream2)
            {
                return true;
            }

            ArgumentNullException.ThrowIfNull(stream1);
            ArgumentNullException.ThrowIfNull(stream2);

            if (stream1.Length != stream2.Length)
            {
                return false;
            }

            stream1.Seek(0, SeekOrigin.Begin);
            stream2.Seek(0, SeekOrigin.Begin);

            const int bufferLength = 1024 * 1024;
            byte[] buffer1 = new byte[bufferLength];
            byte[] buffer2 = new byte[bufferLength];

            for (long i = 0; i < stream1.Length; i++)
            {
                Array.Clear(buffer1, 0, bufferLength);
                Array.Clear(buffer2, 0, bufferLength);

                int read1 = await stream1.ReadAsync(buffer1, 0, bufferLength);
                int read2 = await stream2.ReadAsync(buffer2, 0, bufferLength);

                if (read1 != read2)
                {
                    return false;
                }

                i += read1;

                if (!UnsafeCompare(buffer1, buffer2))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Provides a fast mechanism to compare byte arrays.
        /// Reference: https://stackoverflow.com/a/8808245/3132415
        /// </summary>
        /// <param name="array1">First <see cref="byte[]"/> to compare.</param>
        /// <param name="array2">Second <see cref="byte[]"/> to compare.</param>
        /// <returns><see cref="true"/> if content of both arrays is equal, <see cref="false"/> otherwise.</returns>
        public static unsafe bool UnsafeCompare(byte[] array1, byte[] array2)
        {
            if (array1 == array2)
            {
                return true;
            }

            if (array1 == null || array2 == null || array1.Length != array2.Length)
            {
                return false;
            }

            fixed (byte* p1 = array1, p2 = array2)
            {
                byte* x1 = p1, x2 = p2;
                int l = array1.Length;
                for (int i = 0; i < l / 8; i++, x1 += 8, x2 += 8)
                {
                    if (*((long*)x1) != *((long*)x2))
                    {
                        return false;
                    }
                }

                if ((l & 4) != 0)
                {
                    if (*((int*)x1) != *((int*)x2))
                    {
                        return false;
                    }

                    x1 += 4;
                    x2 += 4;
                }

                if ((l & 2) != 0)
                {
                    if (*((short*)x1) != *((short*)x2))
                    {
                        return false;
                    }

                    x1 += 2;
                    x2 += 2;
                }

                if ((l & 1) != 0)
                {
                    if (*((byte*)x1) != *((byte*)x2))
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
