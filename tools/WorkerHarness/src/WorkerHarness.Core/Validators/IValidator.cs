// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core.Validators
{
    /// <summary>
    /// An abtraction of a validator
    /// </summary>
    public interface IValidator
    {
        /// <summary>
        /// Validate a StreamingMessage with a given validation context
        /// </summary>
        /// <param name="context" cref="ValidationContext">context of the validation</param>
        /// <param name="message" cref="StreamingMessage">message to validate against</param>
        /// <returns>true if validation succeeds, false otherwise</returns>
        bool Validate(ValidationContext context, object message);
    }
}