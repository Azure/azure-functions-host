using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace TriggerService
{
    // !!! This goes in its own nuget package, shared by C# clients?
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

        // !!! Include a password cookie here? 
        // This gets included as a header in all callbacks, client can use to validate. 
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
        // Blob path
        public string BlobInput { get; set; }
        public string BlobOutput { get; set; }

        // Valid for TriggerType.Queue
        // Queue 
        public string QueueName { get; set; }

        // Valid for TriggerType.Timer
        // Timer
        public TimeSpan? Interval { get; set; }

        public static TriggerRaw NewBlob(string callbackPath, string blobInput, string blobOutput = null)
        {
            return new TriggerRaw
            {
                Type = TriggerType.Blob,
                BlobInput = blobInput,
                BlobOutput = blobOutput
            };
        }

        public static TriggerRaw NewQueue(string callbackPath, string queueName)
        {
            return new TriggerRaw
            {
                Type = TriggerType.Queue,
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

    // Get Json.Net settings for serializing the TriggerRaw
    // !!! Can we applyy this to TriggerRaw directly?
    public class TriggerJsonNetUtil
    {
        public static JsonSerializerSettings NewSettings()
        {
            // Don't care about TypeNameHandling since we don't use polymorphism
            var settings = new JsonSerializerSettings()
            {
                NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                Formatting = Formatting.Indented
            };

            return settings;
        }

        public static JsonSerializerSettings _settings = NewSettings();

        public static JsonSerializerSettings SerializerSettings
        {
            get
            {
                return _settings;
            }
        }
    }
}