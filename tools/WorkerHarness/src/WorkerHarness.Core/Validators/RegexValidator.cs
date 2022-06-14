using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core.Validators
{
    internal class RegexValidator : IValidator
    {
        public bool Validate(ValidationContext context, StreamingMessage message)
        {
            throw new NotImplementedException();
        }
    }
}
