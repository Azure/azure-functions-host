namespace WorkerHarness.Core
{
    /// <summary>
    /// Abtract the responsibility to create a validator
    /// </summary>
    public interface IValidatorManager
    {
        /// <summary>
        /// Create a validator based on a given type
        /// </summary>
        /// <param name="validatorType" cref="string">the type of validator</param>
        /// <returns></returns>
        IValidator Create(string validatorType);
    }
}
