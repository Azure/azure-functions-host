using System;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests
{
    public static class ExceptionAssert
    {
        public static void ThrowsArgument(Action action, string expectedParameterName, string expectedMessage)
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() => action.Invoke());
            Assert.Equal(expectedParameterName, exception.ParamName);
            string fullExpectedMessage = String.Format("{0}{1}Parameter name: {2}", expectedMessage,
                Environment.NewLine, expectedParameterName);
            Assert.Equal(fullExpectedMessage, exception.Message);
        }

        public static void ThrowsInvalidOperation(Action action, string expectedMessage)
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => action.Invoke());
            Assert.Equal(expectedMessage, exception.Message);
        }
    }
}
