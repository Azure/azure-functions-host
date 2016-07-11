// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using YamlDotNet.Serialization;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class ParseUtility
    {
    
        private const string DefaultHttpTriggerName = "req";
        private const string DefaultHttpResponseName = "res";
        private const string DefaultTimerTriggerName = "timerInfo";

        public static BindingMetadata GetTriggerBindingMetadata(string trigger)
        {
            //parse the trigger template to fill out trigger information
            var triggerTemplate = trigger;
            string[] triggerParts = triggerTemplate.Trim(new char[] { ']', '[' }).Split('@');
            BindingMetadata triggerBindingMetadata;
            if (triggerParts[0].Equals("TIMER", StringComparison.OrdinalIgnoreCase))
            {
                //it is a timer action
                triggerBindingMetadata = new TimerBindingMetadata();
                triggerBindingMetadata.Type = BindingType.TimerTrigger;
                ((TimerBindingMetadata)triggerBindingMetadata).Schedule = triggerParts[1];
            }
            else
            {
                triggerBindingMetadata = new HttpTriggerBindingMetadata();
                triggerBindingMetadata.Type = BindingType.HttpTrigger;
                //use the route template from the route/method template
                ((HttpTriggerBindingMetadata)triggerBindingMetadata).Route = triggerParts[1];
                //get the method tag from the route/method template
                Collection<HttpMethod> methods = new Collection<HttpMethod>();
                methods.Add(new HttpMethod(triggerParts[0]));
                ((HttpTriggerBindingMetadata) triggerBindingMetadata).Methods = methods;
                ((HttpTriggerBindingMetadata) triggerBindingMetadata).AuthLevel = AuthorizationLevel.Anonymous;
            }
            return triggerBindingMetadata;
        }

        public static void CreateDefaultBindings(FunctionMetadata functionMetadata, BindingMetadata triggerBindingMetadata)
        {
            var triggerType = triggerBindingMetadata.Type;
            if (triggerType == BindingType.HttpTrigger)
            {
                //add in http trigger if they didn't specify it
                var httpTrigger = functionMetadata.Bindings.FirstOrDefault(
                    p => p.Type == BindingType.HttpTrigger && p.Direction == BindingDirection.In);
                if (httpTrigger == null)
                {
                    httpTrigger = triggerBindingMetadata;
                    httpTrigger.Direction = BindingDirection.In;
                    httpTrigger.Name = DefaultHttpTriggerName;
                    httpTrigger.Type = BindingType.HttpTrigger;
                    functionMetadata.Bindings.Add(httpTrigger);
                }
                //add in http response if they didn't specify it
                var response =
                    functionMetadata.Bindings.FirstOrDefault(
                        p => p.Type == BindingType.Http && p.Direction == BindingDirection.Out);
                if (response == null)
                {
                    response = new HttpBindingMetadata()
                    {
                        Direction = BindingDirection.Out,
                        Name = DefaultHttpResponseName,
                        Type = BindingType.Http
                    };
                    functionMetadata.Bindings.Add(response);
                }
            }
            else if (triggerType == BindingType.TimerTrigger)
            {
                var timerTrigger = functionMetadata.Bindings.FirstOrDefault(
                    p => p.Type == BindingType.TimerTrigger && p.Direction == BindingDirection.In);
                if (timerTrigger == null)
                {
                    timerTrigger = triggerBindingMetadata;
                    timerTrigger.Direction = BindingDirection.In;
                    timerTrigger.Name = DefaultTimerTriggerName;
                    timerTrigger.Type = BindingType.TimerTrigger;
                    functionMetadata.Bindings.Add(timerTrigger);
                }
            }
        }
    }

    //Schema for the Yaml object
    public class ApiConfig
    {      
        public string Language { get; set; }
        public TableDetails TableStorage { get; set; }
        public string CommonCode { get; set; }
        //supressed to allow yaml parser to assign a value
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public Collection<FunctionDetails> Functions { get; set; }
    }

    public class TableDetails
    {
        public string Table { get; set; }
        public string PartitionKey { get; set; }
        public string Connection { get; set; }

    }

    public class FunctionDetails
    {
        private Collection<string> _bindingStrings;
        private Collection<BindingDetail> _bindingDetails;
        public string Name { get; set; }
        public string Trigger { get; set; }
        public string Code { get; set; }
        public string CodeLocation { get; set; }
        //supressed to allow yaml parser to assign a value to the property
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage",
            "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [YamlMember(Alias = "bindings")]
        public Collection<string> BindingStrings
        {
            get { return _bindingStrings; }
            set
            {
                _bindingStrings = value;
                _bindingDetails = new Collection<BindingDetail>();
                foreach (string bindingString in _bindingStrings)
                {
                    string[] bindingParts = bindingString.Split(new char[] { ':', '-' });
                    if (bindingParts.Length == 3)
                    {
                        var bindingDetail = new BindingDetail()
                        {
                            Name = bindingParts[0],
                            BindingType = bindingParts[1],
                            Direction = bindingParts[2]
                        };
                        _bindingDetails.Add(bindingDetail);
                    }
                }
            }
        }
        public Collection<BindingDetail> BindingDetails
        {
            get { return _bindingDetails; }
        }
    }


    public class BindingDetail
    {
        public string Name { get; set; }
        public string BindingType { get; set; }
        public string Direction { get; set; }
    }
    
}


