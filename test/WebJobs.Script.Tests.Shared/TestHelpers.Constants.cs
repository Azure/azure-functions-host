// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public static partial class TestHelpers
    {
        [SuppressMessage("Microsoft.Security", "CS002:SecretInNextLine", Justification = "Well known account key for emulator. Used for testing.")]
        public static readonly string EmulatorAccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

        public static readonly string EncryptionKey = _encryptionKey.Value;

        private static readonly Lazy<string> _encryptionKey = new Lazy<string>(
            () =>
            {
                using Aes aes = Aes.Create();
                aes.GenerateKey();
                return Convert.ToBase64String(aes.Key);
            });

        /// <summary>
        /// Gets the common root directory that functions tests create temporary directories under.
        /// This enables us to clean up test files by deleting this single directory.
        /// </summary>
        public static string FunctionsTestDirectory
        {
            get
            {
                return Path.Combine(Path.GetTempPath(), "FunctionsTest");
            }
        }
    }
}
