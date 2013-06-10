using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace TriggerService
{    
    /// <summary>
    /// Define the kind of trigger
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum TriggerType
    {
        /// <summary>
        /// Blob trigger, invoked when an input blob is detected. 
        /// </summary>
        Blob = 1,

        /// <summary>
        /// Queue Trigger, invoked when a new queue mesasge is detected
        /// </summary>
        Queue = 2,

        /// <summary>
        /// Timer trigger, invoked when a timer is fired. 
        /// </summary>
        Timer = 3,
    }

    /// <summary>
    /// Client returns this to register new triggers.  
    /// </summary>
    public class AddTriggerPayload
    {
        /// <summary>
        /// Provide credential information for the azure storage that the triggers bind against.
        /// </summary>
        public Credentials Credentials { get; set; }

        /// <summary>
        /// Collection of new triggers to register. 
        /// </summary>
        public TriggerRaw[] Triggers { get; set; }

        // $$$ Include a password cookie here? 
        // This gets included as a header in all callbacks, client can use to authenticate. 

        /// <summary>
        /// Helper to validate the structure is proper before intializing. Will throw on errors.
        /// </summary>
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

    /// <summary>
    /// Store all sensitive information in one spot.  
    /// </summary>
    public class Credentials
    {
        /// <summary>
        /// The azure storage account connection string that blob and queue triggers bind against. 
        /// </summary>
        public string AccountConnectionString { get; set; }
    }
        
    /// <summary>
    /// Wire protocol for an serializing triggers.
    /// Irrelevant fields should reamin null.
    /// </summary>
    public class TriggerRaw
    {
        /// <summary>
        /// Define what type of trigger. 
        /// Serializing can emit as either the raw number or the name.
        /// </summary>
        public TriggerType Type { get; set; }

        /// <summary>
        /// Invoke this path when the trigger fires 
        /// </summary>
        public string CallbackPath { get; set; }
        
        /// <summary>
        /// For Blobs, blob path for the input. This is of the form "container/blob"
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string BlobInput { get; set; }
                
        /// <summary>
        /// For Blobs, optional. semicolon delimited list of blob paths for the output. This is of the form 
        /// "container1/blob1;container2/blob2"
        /// The trigger is not fired if all outputs have a more recent modified timestamp than the input. 
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string BlobOutput { get; set; }

        /// <summary>
        /// For Queues. The name of the azure queue. Be sure to follow azure queue naming rules, including all lowercase.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string QueueName { get; set; }

        /// <summary>
        /// For timers, the interval between when the time is fired. 
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public TimeSpan? Interval { get; set; }

        /// <summary>
        /// Create a new trigger on blobs. This fires the callback if a new input blob is detected. The http content is the string name of the blob path that was detected. 
        /// For example, if input is 'container/{name}.txt', and output is 'container/outputs/{nane}.txt;
        /// </summary>
        /// <param name="callbackPath">The uri to get invoked when this trigger fires.</param>
        /// <param name="blobInput">An input path to search for. The blob name can include 'route parameters' for pattern matching, is and of the form 'container/blob'. </param>
        /// <param name="blobOutput">A semicolon delimited list of output paths. The trigger is not fired if all outputs are newer than the input. 
        /// This can have route parameters which are populated from the capture at the input values.</param>
        /// <returns>A trigger object.</returns>
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

        /// <summary>
        /// Create a new trigger on queue message. This fires the callback when a new queue message is detected, where the http request contents are the azure queue message contents. 
        /// The azure message is deleted if the callback is invoked. 
        /// </summary>
        /// <param name="callbackPath">The uri to get invoked when this trigger fires.</param>
        /// <param name="queueName">The azure queue to listen on. Be sure to adhere to azure queue naming rules, including being all lowercase.</param>
        /// <returns>A trigger object.</returns>
        public static TriggerRaw NewQueue(string callbackPath, string queueName)
        {
            return new TriggerRaw
            {
                Type = TriggerType.Queue,
                CallbackPath = callbackPath,
                QueueName = queueName
            };
        }

        /// <summary>
        /// Create a trigger that fires on a timer interval. 
        /// </summary>
        /// <param name="callbackPath">The uri to get invoked when this trigger fires.</param>
        /// <param name="interval">The frequency at which to invoke the timer</param>
        /// <returns>A trigger object.</returns>
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