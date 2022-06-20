using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public override void ConstructExpression()
        {
            SetExpression(Expected);
        }
    }
}
