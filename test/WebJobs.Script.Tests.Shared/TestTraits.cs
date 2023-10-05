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

        /// <summary>
        /// Release Tests only run in branches: release.
        /// </summary>
        public const string ReleaseTests = "ReleaseTests";

        public const string SamplesEndToEnd = "SamplesEndToEndTests";

        /// <summary>
        /// Drain mode specific tests.
        /// </summary>
        public const string DrainModeEndToEnd = "DrainModeEndToEndTests";

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

        /// <summary>
        /// Tests for the AdminIsolation feature.
        /// </summary>
        public const string AdminIsolationTests = "AdminIsolationTests";

        /// <summary>
        /// Tests for the FunctionsController.
        /// </summary>
        public const string FunctionsControllerEndToEnd = "FunctionsControllerEndToEnd";

        public const string FlexConsumptionMetricsTests = "FlexConsumptionMetricsTests";
    }
}
