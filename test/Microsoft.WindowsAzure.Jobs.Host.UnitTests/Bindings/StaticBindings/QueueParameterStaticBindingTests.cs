using System;
using Microsoft.WindowsAzure.Jobs.Host.TestCommon;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests.Bindings.StaticBindings
{
    public class QueueParameterStaticBindingTests
    {
        [Fact]
        public void Bind_WithoutBlobNameOrTrigger_Throws()
        {
            // Arrange
            QueueParameterStaticBinding product = new QueueParameterStaticBinding
            {
                IsInput = true
            };
            IRuntimeBindingInputs inputs = new RuntimeBindingInputs(String.Empty);

            // Act & Assert
            ExceptionAssert.ThrowsInvalidOperation(() => product.Bind(inputs),
                "Direct calls are not supported for QueueInput methods.");
        }
    }
}
