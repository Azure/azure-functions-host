using System;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Bindings.StaticBindings
{
    public class BlobParameterStaticBindingTests
    {
        [Fact]
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
