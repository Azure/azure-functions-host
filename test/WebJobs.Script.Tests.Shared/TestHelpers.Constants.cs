// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public static partial class TestHelpers
    {
        [SuppressMessage("Microsoft.Security", "CS002:SecretInNextLine", Justification = "Used for validating encryption behavior.")]
        public const string FakeEncryptionKey = "/a/vXvWJ3Hzgx4PFxlDUJJhQm5QVyGiu0NNLFm/ZMMg=";

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
