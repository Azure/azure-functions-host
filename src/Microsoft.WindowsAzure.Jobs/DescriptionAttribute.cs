using System;

namespace Microsoft.WindowsAzure.Jobs
{
    /// <summary>
    /// Represents an attribute that is used to provide a description for a Job function.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class DescriptionAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the DescriptionAttribute class with a description.
        /// </summary>
        /// <param name="description">The description of the Job function.</param>
        public DescriptionAttribute(string description)
        {
            Description = description;
        }

        /// <summary>
        /// Gets or sets the description of the Job function.
        /// </summary>
        public string Description { get; private set; }
    }
}
