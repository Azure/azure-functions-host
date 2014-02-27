using Xunit;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests.Bindings.BindResults
{
    public class NullBindResultTests
    {
        [Fact]
        public void GetStatus_MessageHasNewLines_EncodedCorrectly()
        {
            // Arrange
            var message = "Some\r\nmessage\r\nwith\r\nnew lines";
            var expectedMessage = "Some; message; with; new lines";

            // Act
            var actual = ((ISelfWatch)new NullBindResult(message) { IsErrorResult = true }).GetStatus();

            // Assert
            Assert.Equal(expectedMessage, actual);
        }
    }
}
