// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Variables;

namespace WorkerHarness.Core.Matching
{
    /// <summary>
    /// Encapsulate the criteria to match a Message to a Grpc StreamingMessage
    /// </summary>
    public sealed class MatchingContext : ExpressionBase
    {
        // the type of the match. The default is string comparison.
        public string Type { get; set; } = "string";

        // The property of a Grpc StreamingMessage to match against
        public string Query { get; set; } = string.Empty;

        // The expected value of the property being queried
        public string Expected { get; set; } = string.Empty;

        /// <summary>
        /// Set Expected property to be an expression
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