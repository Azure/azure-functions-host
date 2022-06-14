using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    /// <summary>
    /// Encapsulates the validation context so that a validator can use to validate a message
    /// </summary>
    public class ValidationContext
    {
        // The type of validator to use
        public string? Type { get; set; }

        // The property to validate
        public string? Query { get; set; }

        // The expected value of the property
        public string? Expected { get; set; }

        // Wrap the expected value in an Expression object
        public Expression? ExpectedExpression { get; set; }
    }
}
