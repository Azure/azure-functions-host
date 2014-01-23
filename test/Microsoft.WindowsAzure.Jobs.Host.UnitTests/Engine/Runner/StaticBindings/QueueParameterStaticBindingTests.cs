using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs.Host.TestCommon;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests.Engine.Runner.StaticBindings
{
    [TestClass]
    public class QueueParameterStaticBindingTests
    {
        [TestMethod]
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
