// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class JObjectExtensionsTests
    {
        public enum NodeType
        {
            Windows,
            Linux
        }

        [Fact]
        public void DefaultCameCaseParsing()
        {
            var source = JObject.FromObject(GetDefaultParent());
            var converted = source.ToCamelCase();
            // SelectToken is case sensitive
            Assert.Equal("ParentItem", converted.SelectToken("parentName").ToString());
            Assert.Null(converted.SelectToken("ParentName"));
            Assert.Equal(10, Convert.ToInt32(converted.SelectToken("parentAge").ToString()));
            Assert.Equal("ParentNode", converted.SelectToken("supportNode.description").ToString());

            Assert.Equal(TimeSpan.FromSeconds(10).ToString(), converted.SelectToken("supportNode.timeoutDuration").ToString());
            Assert.Equal("ChildItem01", converted.SelectToken("supportChildren[0].childName").ToString());
            Assert.Equal("1", converted.SelectToken("supportChildren[1].supportNode.nodeType").ToString());
        }

        [Fact]
        public void NullProperty()
        {
            var source = JObject.FromObject(new Parent());
            var converted = source.ToCamelCase();
            Assert.Equal(string.Empty, converted.SelectToken("parentName").ToString());
            Assert.Equal("0", converted.SelectToken("parentAge").ToString());
        }

        private Parent GetDefaultParent()
        {
            return new Parent
            {
                ParentName = "ParentItem",
                ParentAge = 10,
                SupportNode = new Node()
                {
                    Description = "ParentNode",
                    TimeoutDuration = TimeSpan.FromSeconds(10),
                    NodeType = NodeType.Windows,
                },
                SupportChildren = new List<Child>()
                {
                    new Child()
                    {
                        ChildName = "ChildItem01",
                        ChildAge = 5,
                        SupportNode = new Node()
                        {
                            Description = "ChildNode01",
                            TimeoutDuration = TimeSpan.FromSeconds(5),
                            NodeType = NodeType.Windows,
                        }
                    },
                    new Child()
                    {
                        ChildName = "ChildItem02",
                        ChildAge = 3,
                        SupportNode = new Node()
                        {
                            Description = "ChildNode02",
                            TimeoutDuration = TimeSpan.FromSeconds(3),
                            NodeType = NodeType.Linux,
                        }
                    }
                }
            };
        }

        public class Parent
        {
            public string ParentName { get; set; }

            public int ParentAge { get; set; }

            public Node SupportNode { get; set; }

            public IList<Child> SupportChildren { get; set; }
        }

        public class Child
        {
            public string ChildName { get; set; }

            public int ChildAge { get; set; }

            public Node SupportNode { get; set; }
        }

        public class Node
        {
            public string Description { get; set; }

            public TimeSpan TimeoutDuration { get; set; }

            public NodeType NodeType { get; set; }
        }
    }
}
