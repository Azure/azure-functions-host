// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.WebJobs.Script.Tests
{
    public static class TestTraits
    {
        /// <summary>
        /// Classifies a category of tests. A category may have multiple groups.
        /// </summary>
        public const string Category = "Category";

        /// <summary>
        /// Defines a group of tests to be run together. Useful for test isolation.
        /// </summary>
        public const string Group = "Group";

        public const string EndToEnd = "E2E";

        public const string SamplesEndToEnd = "SamplesEndToEndTests";

        /// <summary>
        /// Standby mode tests are special in that they set uni-directional
        /// static state, and benefit from test isolation.
        /// </summary>
        public const string StandbyModeTestsLinux = "StandbyModeEndToEndTests_Linux";

        /// <summary>
        /// Standby mode tests are special in that they set uni-directional
        /// static state, and benefit from test isolation.
        /// </summary>
        public const string StandbyModeTestsWindows = "StandbyModeEndToEndTests_Windows";

        /// <summary>
        /// These are Linux container environment specific tests.
        /// </summary>
        public const string ContainerInstanceTests = "ContainerInstanceTests";
    }
}
