using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace TriggerService
{    
    [JsonConverter(typeof(StringEnumConverter))]
    public enum TriggerType
    {
        Blob = 1,
        Queue = 2,
        Timer = 3,
    }

    // Client returns this to register triggers. 
    public class AddTriggerPayload
    {
        public Credentials Credentials { get; set; }
        public TriggerRaw[] Triggers { get; set; }

        // $$$ Include a password cookie here? 
        // This gets included as a header in all callbacks, client can use to authenticate. 

        // Helper to validate the structure is proper before intializing. 
        public void Validate()
        {
            int i = 0;
            foreach (var trigger in Triggers)
            {
                i++;

                try
                {
                    Verify(trigger.CallbackPath != null, "Must set callback path");
                    new Uri(trigger.CallbackPath); // verify we can parse URL.

                    switch (trigger.Type)
                    {
                        case TriggerType.Timer:
                            Verify(trigger.Interval.HasValue, "Timer trigger is missing the interval value");
                            Verify(trigger.Interval.Value > MinInterval, string.Format("Timer interval can't be less than {0}", MinInterval));

                            VerifyNotBlob(trigger);
                            VerifyNotQueue(trigger);
                            break;

                        case TriggerType.Blob:
                            Verify(trigger.BlobInput != null, "Blob trigger is missing blob input");
                            Verify(string.Compare(trigger.BlobInput, trigger.BlobOutput, true) != 0, "Blob trigger output is identical to input");

                            VerifyNotTimer(trigger);
                            VerifyNotQueue(trigger);
                            break;

                        case TriggerType.Queue:
                            Verify(trigger.QueueName != null, "Queue trigger is missing queue name");
                            ValidateQueueName(trigger.QueueName);

                            VerifyNotTimer(trigger);
                            VerifyNotBlob(trigger);
                            break;

                        default:
                            Verify(false, string.Format("Unrecognized trigger type '{0}'", trigger.Type));
                            break;
                    }
                }
                catch (Exception e)
                {
                    string prefix = string.Format("Error in trigger #{0},", i);
                    throw new InvalidOperationException(prefix + e.Message);
                }
            }
        }

        void VerifyNotBlob(TriggerRaw trigger)
        {
            Verify(trigger.BlobOutput == null, string.Format(WrongFieldFormat, trigger.Type, "BlobOutput"));
            Verify(trigger.BlobInput == null, string.Format(WrongFieldFormat, trigger.Type, "BlobInput"));
        }

        void VerifyNotQueue(TriggerRaw trigger)
        {
            Verify(trigger.QueueName == null, string.Format(WrongFieldFormat, trigger.Type, "QueueName"));
        }

        void VerifyNotTimer(TriggerRaw trigger)
        {
            Verify(trigger.Interval == null, string.Format(WrongFieldFormat, trigger.Type, "Interval"));
        }

        private const string WrongFieldFormat = "Trigger type '{0}' should not set '{1}' field.";
        private static TimeSpan MinInterval = TimeSpan.FromMinutes(1);

        void Verify(bool f, string msg)
        {
            if (!f)
            {
                throw new InvalidOperationException(msg);
            }
        }

        // This function from: http://blogs.msdn.com/b/neilkidd/archive/2008/11/11/windows-azure-queues-are-quite-particular.aspx
        // See http://msdn.microsoft.com/library/dd179349.aspx for rules to enforce.
        /// <summary>
        /// Ensures that the passed name is a valid queue name.
        /// If not, an ArgumentException is thrown
        /// </summary>
        /// <exception cref="System.ArgumentException">If the name is invalid</exception>
        /// <param name="name">The name to be tested</param>
        private static void ValidateQueueName(string name)
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

    // Credentialling. Store all sensitive information in one spot. 
    public class Credentials
    {
        // Could later generalize this to multiple accounts.
        public string AccountConnectionString { get; set; }
    }

    // Raw wire protocol for serializing triggers. 
    // Serialization formats don't have a standard way of doing polymorphism, 
    // so this is a flat struct with a type field.
    // When serializing, can omit any null fields. 
    // No password information in here. 
    public class TriggerRaw
    {
        // Serializing can emit as either the raw number or the name
        public TriggerType Type { get; set; }

        // Common properties
        // Invoke this path when the trigger fires 
        public string CallbackPath { get; set; }

        // Valid for TriggerType.Blob
        // Blob path for input. 
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string BlobInput { get; set; }

        // Can be null, or multiple paths separate by ';'
        // If set, then Input must be newer than at least one output in order to trigger. 
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string BlobOutput { get; set; }

        // Valid for TriggerType.Queue
        // Queue 
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string QueueName { get; set; }

        // Valid for TriggerType.Timer
        // Timer
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public TimeSpan? Interval { get; set; }

        public static TriggerRaw NewBlob(string callbackPath, string blobInput, string blobOutput = null)
        {
            return new TriggerRaw
            {
                Type = TriggerType.Blob,
                CallbackPath = callbackPath,
                BlobInput = blobInput,
                BlobOutput = blobOutput
            };
        }

        public static TriggerRaw NewQueue(string callbackPath, string queueName)
        {
            return new TriggerRaw
            {
                Type = TriggerType.Queue,
                CallbackPath = callbackPath,
                QueueName = queueName
            };
        }

        public static TriggerRaw NewTimer(string callbackPath, TimeSpan interval)
        {
            return new TriggerRaw
            {
                Type = TriggerType.Timer,
                Interval = interval,
                CallbackPath = callbackPath
            };
        }
    }
}