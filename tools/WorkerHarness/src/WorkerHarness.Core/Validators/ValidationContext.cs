// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Variables;

namespace WorkerHarness.Core.Validators
{
    /// <summary>
    /// Encapsulates the validation context to validate a message
    /// </summary>
    public class ValidationContext : ExpressionBase
    {
        // The type of validator to use. Default to string validator
        public string Type { get; set; } = "string";

        // The property to validate
        public string Query { get; set; } = string.Empty;

        // The expected value of the property
        public string Expected { get; set; } = string.Empty;

        /// <summary>
        /// Set the Expected property to be an Expression
        /// </summary>
        public override void ConstructExpression()
        {
            try
            {
                SetExpression(Expected);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException(string.Format("Failed to construct an expression from the expected value {0}. {1}", Expected, ex.Message));
            }
        }
    }
}
