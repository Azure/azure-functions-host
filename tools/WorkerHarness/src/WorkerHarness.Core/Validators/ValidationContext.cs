// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core
{
    /// <summary>
    /// Encapsulates the validation context to validate a message
    /// </summary>
    public class ValidationContext : Expression
    {
        // The type of validator to use
        public string Type { get; set; } = string.Empty;

        // The property to validate
        public string Query { get; set; } = string.Empty;

        // The expected value of the property
        public string Expected { get; set; } = string.Empty;

        /// <summary>
        /// Set the Expected property to be an Expression
        /// </summary>
        public override void ConstructExpression()
        {
            SetExpression(Expected);
        }
    }
}
