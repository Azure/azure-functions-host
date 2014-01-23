using System;

namespace Microsoft.WindowsAzure.Jobs
{
    /// <summary>Defines a command that occurs at an interval that may change with every execution.</summary>
    internal interface IIntervalSeparationCommand
    {
        /// <summary>Returns the current interval to wait before running <see cref="Execute"/> again.</summary>
        TimeSpan SeparationInterval { get; }

        /// <summary>Executes the command.</summary>
        /// <remarks>Calling this method may result in an updated <see cref="SeparationInterval"/>.</remarks>
        void Execute();
    }
}
