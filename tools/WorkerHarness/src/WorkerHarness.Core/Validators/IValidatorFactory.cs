// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core.Validators
{
    /// <summary>
    /// Abtract the responsibility to create a validator
    /// </summary>
    public interface IValidatorFactory
    {
        /// <summary>
        /// Create a validator based on a given type
        /// </summary>
        /// <param name="validatorType" cref="string">the type of validator</param>
        /// <returns></returns>
        IValidator Create(string validatorType);
    }
}
