using System;

namespace RunnerInterfaces
{
    static partial class Utility
    {
        // This function from: http://blogs.msdn.com/b/neilkidd/archive/2008/11/11/windows-azure-queues-are-quite-particular.aspx
        // See http://msdn.microsoft.com/library/dd179349.aspx for rules to enforce.
        /// <summary>
        /// Ensures that the passed name is a valid queue name.
        /// If not, an ArgumentException is thrown
        /// </summary>
        /// <exception cref="System.ArgumentException">If the name is invalid</exception>
        /// <param name="name">The name to be tested</param>
        public static void ValidateQueueName(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw new ArgumentException(
                    "A queue name can't be null or empty", "name");
            }

            // A queue name must be from 3 to 63 characters long.
            if (name.Length < 3 || name.Length > 63)
            {
                throw new ArgumentException(
                    "A queue name must be from 3 to 63 characters long - \""
                    + name + "\"", "name");
            }

            // The dash (-) character may not be the first or last letter.
            // we will check that the 1st and last chars are valid later on.
            if (name[0] == '-' || name[name.Length - 1] == '-')
            {
                throw new ArgumentException(
                    "The dash (-) character may not be the first or last letter - \""
                    + name + "\"", "name");
            }

            // A queue name must start with a letter or number, and may 
            // contain only letters, numbers and the dash (-) character
            // All letters in a queue name must be lowercase.
            foreach (Char ch in name)
            {
                if (Char.IsUpper(ch))
                {
                    throw new ArgumentException(
                        "Queue names must be all lower case - \""
                        + name + "\"", "name");
                }
                if (Char.IsLetterOrDigit(ch) == false && ch != '-')
                {
                    throw new ArgumentException(
                        "A queue name can contain only letters, numbers, "
                        + "and and dash(-) characters - \""
                        + name + "\"", "name");
                }
            }
        }
    }
}