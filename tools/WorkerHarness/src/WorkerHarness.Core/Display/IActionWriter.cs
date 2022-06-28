using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;

namespace WorkerHarness.Core
{
    /// <summary>
    /// Provide an abstraction to write action messages
    /// </summary>
    public interface IActionWriter
    {
        /// <summary>
        /// Write success message
        /// </summary>
        /// <param name="message"></param>
        void WriteSuccess(string message);

        /// <summary>
        /// Write error message
        /// </summary>
        /// <param name="message"></param>
        void WriteError(string message);

        /// <summary>
        /// Write information message
        /// </summary>
        /// <param name="message"></param>
        void WriteInformation(string message);
    }
}