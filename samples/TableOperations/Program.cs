// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.StorageClient;

namespace TableOperations
{
    class WordCount
    {
        public string Word { get; set; }

        public int Count { get; set; }
    }

    class Program
    {
        /// <summary>
        /// Creates the frequency table for the words in the input string and then splits the phrase in words
        /// </summary>
        public static void CountAndSplitInWords([QueueInput] string textInput, [Table] IDictionary<Tuple<string, string>, WordCount> words, [QueueOutput("words")] out IEnumerable<string> wordsQueue)
        {
            // Normalize the capitalization
            textInput = textInput.ToLower();

            // Split in words (assume words are only delimited by space)
            string[] wordsCollection = textInput.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // Create word groups (one group/word)
            var wordCount = wordsCollection.GroupBy(w => w);

            foreach (var group in wordCount)
            {
                // The data in the storage table has 
                //      PartitionKey = the first letter of the word
                //      RowKey = the full word
                string parititonKey = group.Key[0].ToString();
                string rowKey = group.Key;
                Tuple<string, string> partitionRowKey = new Tuple<string, string>(parititonKey, rowKey);

                // If the row already exists, increment the Count. 
                // Otherwise, create a new row and set the Count to the current value
                if (words.ContainsKey(partitionRowKey))
                {
                    words[partitionRowKey].Count += group.Count();
                }
                else
                {
                    words.Add(partitionRowKey, new WordCount() { Word = group.Key, Count = group.Count() });
                }
            }

            // Enqueue distinct words (no duplicates)
            wordsQueue = wordCount.Select(g => g.Key);
        }

        /// <summary>
        /// Counts the frequency of characters in a word (triggered by messages created by "CountAndSplitInWords")
        /// </summary>
        public static void CharFrequency([QueueInput("words")] string word)
        {
            // Create a dictionary of character frequencies
            //      Key = the character
            //      Value = number of times that character appears in a word
            IDictionary<char, int> frequency = word
                .GroupBy(c => c)
                .ToDictionary(group => group.Key, group => group.Count());

            Console.WriteLine("The frequency of letters in the word \"{0}\" is: ", word);
            foreach (var character in frequency)
            {
                Console.WriteLine("{0}: {1}", character.Key, character.Value);
            }
        }

        [NoAutomaticTrigger]
        public static void ManualTrigger([Table("log")] IDictionary<Tuple<string, string>, object> data)
        {
            DateTime dt = DateTime.Now;
            data.Add(new Tuple<string, string>(dt.Year.ToString(), dt.Month.ToString()), dt);
        }

        static void Main()
        {
            CreateDemoData();

            JobHost host = new JobHost();
            host.Call(typeof(Program).GetMethod("ManualTrigger"));

            host.RunAndBlock();
        }

        private static void CreateDemoData()
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(ConfigurationManager.ConnectionStrings["AzureJobsData"].ConnectionString);

            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference("textinput");
            queue.CreateIfNotExist();

            queue.AddMessage(new CloudQueueMessage("Hello hello world"));
        }
    }
}
