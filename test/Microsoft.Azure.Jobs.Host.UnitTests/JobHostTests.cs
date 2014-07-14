using System.Collections.Generic;
using System.Threading;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    public class JobHostUnitTests
    {
        [Fact]
        public void SimpleInvoke_WithDictionary()
        {
            var host = new TestJobHost<ProgramSimple>(null);

            var x = "abc";
            ProgramSimple._value = null;
            host.Call("Test", new Dictionary<string, object> { { "value", x } });

            // Ensure test method was invoked properly.
            Assert.Equal(x, ProgramSimple._value);
        }

        [Fact]
        public void SimpleInvoke_WithObject()
        {
            var host = new TestJobHost<ProgramSimple>(null);

            var x = "abc";
            ProgramSimple._value = null;
            host.Call("Test", new { value = x });

            // Ensure test method was invoked properly.
            Assert.Equal(x, ProgramSimple._value);
        }

        [Fact]
        public void CallWithCancellationToken_PassesCancellationTokenToMethod()
        {
            // Arrange
            ProgramWithCancellationToken.Cleanup();
            var host = new TestJobHost<ProgramWithCancellationToken>(null);

            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                ProgramWithCancellationToken.CancellationTokenSource = source;

                // Act
                host.Call("BindCancellationToken", null, source.Token);

                // Assert
                Assert.True(ProgramWithCancellationToken.IsCancellationRequested);
            }
        }

        [Fact]
        public void CallWithCancellationToken_PassesCancellationTokenToMethodViaIBinder()
        {
            // Arrange
            ProgramWithCancellationToken.Cleanup();
            var host = new TestJobHost<ProgramWithCancellationToken>(null);

            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                ProgramWithCancellationToken.CancellationTokenSource = source;

                // Act
                host.Call("BindCancellationTokenViaIBinder", null, source.Token);

                // Assert
                Assert.True(ProgramWithCancellationToken.IsCancellationRequested);
            }
        }

        class ProgramSimple
        {
            public static string _value; // evidence of execution

            [NoAutomaticTrigger]
            public static void Test(string value)
            {
                _value = value;
            }
        }

        private class ProgramWithCancellationToken
        {
            public static CancellationTokenSource CancellationTokenSource { get; set; }

            public static bool IsCancellationRequested { get; private set; }

            public static void Cleanup()
            {
                CancellationTokenSource = null;
                IsCancellationRequested = false;
            }

            [NoAutomaticTrigger]
            public static void BindCancellationToken(CancellationToken cancellationToken)
            {
                CancellationTokenSource.Cancel();
                IsCancellationRequested = cancellationToken.IsCancellationRequested;
            }

            [NoAutomaticTrigger]
            public static void BindCancellationTokenViaIBinder(IBinder binder)
            {
                CancellationTokenSource.Cancel();
                IsCancellationRequested = binder.CancellationToken.IsCancellationRequested;
            }
        }
    }
}
