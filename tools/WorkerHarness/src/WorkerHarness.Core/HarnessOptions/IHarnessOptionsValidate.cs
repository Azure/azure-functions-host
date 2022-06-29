// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core
{
    /// <summary>
    /// Provides an abtraction to validate a HarnessOptions object
    /// </summary>
    public interface IHarnessOptionsValidate
    {
        /// <summary>
        /// Validate a HarnessOptions object
        /// </summary>
        /// <param name="options" cref="HarnessOptions"></param>
        /// <returns></returns>
        bool Validate(HarnessOptions options);
    }
}
