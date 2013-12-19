using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    internal static class QueueClient
    {
        [DebuggerNonUserCode]
        public static void DeleteQueue(CloudStorageAccount account, string queueName)
        {
            ValidateQueueName(queueName);

            var client = account.CreateCloudQueueClient();
            var q = client.GetQueueReference(queueName);

            DeleteQueue(q);
        }

        [DebuggerNonUserCode]
        public static void DeleteQueue(CloudQueue queue)
        {
            try
            {
                queue.Delete();
            }
            catch (StorageClientException)
            {
            }
        }

        // Apply the callback funciton to each message in the queue. 
        // Delete message from queue once callback returns.
        // Return true if any messages are handled
        //[DebuggerNonUserCode]
        public static bool ApplyToQueue<T>(Action<T> callback, CloudQueue queue)
        {
            IEnumerable<CloudQueueMessage> msgs = null;
            try
            {
                msgs = queue.GetMessages(32); // 32 is max number

            }
            catch
            {
            }
            if (msgs == null)
            {
                return false;
            }

            int count = 0;
            foreach (var msg in msgs)
            {
                T payload;
                try
                {
                    payload = JsonCustom.DeserializeObject<T>(msg.AsString);
                }
                catch
                {
                    // Bad JSON serialization. Posionous. Just remove it.                    
                    queue.DeleteMessage(msg);
                    continue;
                }

                callback(payload);
                count++;

                try
                {
                    queue.DeleteMessage(msg);
                }
                catch { }
            }
            return count > 0;
        }

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
