// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Variables;

namespace WorkerHarness.Core
{
    /// <summary>
    /// Encapsulate the criteria to match a Message to a Grpc StreamingMessage
    /// </summary>
    public class MatchingContext : ExpressionBase
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
            SetExpression(Expected);
        }
    }
}