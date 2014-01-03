using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs.Test;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests.Engine.Runner.StaticBindings
{
    [TestClass]
    public class BlobParameterStaticBindingTests
    {
        [TestMethod]
        public void Bind_WithoutBlobNameOrTrigger_Throws()
        {
            // Arrange
            BlobParameterStaticBinding product = new BlobParameterStaticBinding();
            product.Path = new CloudBlobPath(containerName: "container", blobName: null);
            IRuntimeBindingInputs inputs = new RuntimeBindingInputs(String.Empty);

            // Act & Assert
            ExceptionAssert.ThrowsInvalidOperation(() => product.Bind(inputs),
                "Direct calls are not supported for BlobInput methods bound only to a container name.");
        }
    }
}
