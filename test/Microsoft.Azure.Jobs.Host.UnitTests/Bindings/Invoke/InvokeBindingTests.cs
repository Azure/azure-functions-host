using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Bindings.Invoke;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Bindings.Invoke
{
    public class InvokeBindingTests
    {
        [Fact]
        public void Create_ReturnsNull_IfByRefParameter()
        {
            // Arrange
            string parameterName = "Parameter";
            Type parameterType = typeof(int).MakeByRefType();

            // Act
            IBinding binding = InvokeBinding.Create(parameterName, parameterType);

            // Assert
            Assert.Null(binding);
        }
    }
}
