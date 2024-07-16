// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public static partial class TestHelpers
    {
#if DEBUG
        public const string BuildConfig = "debug";
#else
        public const string BuildConfig = "release";
#endif
        // Not a real storage account key.
        public static readonly string StorageAccountKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("PLACEHOLDER"));

        // Not a real connection string.
        public static readonly string StorageConnectionString = $"DefaultEndpointsProtocol=http;AccountName=fakeaccount;AccountKey={StorageAccountKey}";

        private static readonly Lazy<string> _encryptionKey = new Lazy<string>(
            () =>
            {
                using Aes aes = Aes.Create();
                aes.GenerateKey();
                return Convert.ToBase64String(aes.Key);
            });

        public static string EncryptionKey => _encryptionKey.Value;

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