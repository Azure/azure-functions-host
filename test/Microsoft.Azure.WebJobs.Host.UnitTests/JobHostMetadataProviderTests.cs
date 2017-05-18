// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using Microsoft.Azure.WebJobs.Description;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class JobHostMetadataProviderTests
    {
        [Fact]
        public void Test()
        {
            MyProg prog = new MyProg();
            var activator = new FakeActivator();
            activator.Add(prog);

            JobHostConfiguration config = TestHelpers.NewConfig<MyProg>(activator);
            
            var ext = new TestExtension();

            config.AddExtension(ext);

            IJobHostMetadataProvider metadataProvider = config.CreateMetadataProvider();
            Assert.Equal(1, ext._counter);

            // Callable            
            var host = new TestJobHost<MyProg>(config);
            host.Call("Test");
            Assert.Equal(1, ext._counter);

            // Fact that we registered a Widget converter is enough to add the assembly 
            Assembly asm;
            bool resolved;

            resolved = metadataProvider.TryResolveAssembly(typeof(Widget).Assembly.GetName().Name, out asm);
            Assert.True(resolved);
            Assert.Same(asm, typeof(Widget).Assembly);

            // check with full name 
            resolved = metadataProvider.TryResolveAssembly(typeof(Widget).Assembly.GetName().FullName, out asm);
            Assert.True(resolved);
            Assert.Same(asm, typeof(Widget).Assembly);

            var attrType = metadataProvider.GetAttributeTypeFromName("Test");
            Assert.Equal(typeof(TestAttribute), attrType);

            // JObject --> Attribute 
            var attr = GetAttr<TestAttribute>(metadataProvider, new { Flag = "xyz" });
            Assert.Equal("xyz", attr.Flag);

            // Getting default type. 
            var defaultType = metadataProvider.GetDefaultType(attr, FileAccess.Read, null);
            Assert.Equal(typeof(JObject), defaultType);

            Assert.Throws<InvalidOperationException>(() => metadataProvider.GetDefaultType(attr, FileAccess.Write, typeof(object)));
        }

        static T GetAttr<T>(IJobHostMetadataProvider metadataProvider, object obj) where T : Attribute
        {
            var attribute = metadataProvider.GetAttribute(typeof(T), JObject.FromObject(obj));            
            return (T) attribute;
        }

        [Fact]
        public void AttrBuilder()
        {
            JobHostConfiguration config = TestHelpers.NewConfig();
            var metadataProvider = config.CreateMetadataProvider();

            // Blob 
            var blobAttr = GetAttr<BlobAttribute>(metadataProvider, new { path = "x" } );
            Assert.Equal("x", blobAttr.BlobPath);
            Assert.Equal(null, blobAttr.Access);

            blobAttr = GetAttr<BlobAttribute>(metadataProvider, new { path = "x", direction="in" });
            Assert.Equal("x", blobAttr.BlobPath);
            Assert.Equal(FileAccess.Read, blobAttr.Access);

            blobAttr = GetAttr<BlobAttribute>(metadataProvider, new { Path = "x", Direction="out" });
            Assert.Equal("x", blobAttr.BlobPath);
            Assert.Equal(FileAccess.Write, blobAttr.Access);

            blobAttr = GetAttr<BlobAttribute>(metadataProvider, new { path = "x", direction = "inout" });
            Assert.Equal("x", blobAttr.BlobPath);
            Assert.Equal(FileAccess.ReadWrite, blobAttr.Access);
                        
            blobAttr = GetAttr<BlobAttribute>(metadataProvider, 
            new
            {
                path = "x",
                direction = "in",
                connection = "cx1"
            });               
            Assert.Equal("x", blobAttr.BlobPath);
            Assert.Equal(FileAccess.Read, blobAttr.Access);
            Assert.Equal("cx1", blobAttr.Connection);

            blobAttr = GetAttr<BlobAttribute>(metadataProvider,
              new
              {
                  path = "x",
                  direction = "in",
                  connection = "" // empty, not null 
              });
            Assert.Equal("x", blobAttr.BlobPath);
            Assert.Equal(FileAccess.Read, blobAttr.Access);
            Assert.Equal("", blobAttr.Connection); // empty is passed straight through. 

            var blobTriggerAttr = GetAttr<BlobTriggerAttribute>(metadataProvider, new { path = "x" });
            Assert.Equal("x", blobTriggerAttr.BlobPath);

            // Queue 
            var queueAttr = GetAttr<QueueAttribute>(metadataProvider, new { QueueName = "q1" });
            Assert.Equal("q1", queueAttr.QueueName);

            var queueTriggerAttr = GetAttr<QueueTriggerAttribute>(metadataProvider, new { QueueName = "q1" });
            Assert.Equal("q1", queueTriggerAttr.QueueName);
            
            // Table
            var tableAttr = GetAttr<TableAttribute>(metadataProvider, new { TableName = "t1" });
            Assert.Equal("t1", tableAttr.TableName);

            tableAttr = GetAttr<TableAttribute>(metadataProvider, new { TableName = "t1", partitionKey ="pk", Filter="f1" });
            Assert.Equal("t1", tableAttr.TableName);
            Assert.Equal("pk", tableAttr.PartitionKey);
            Assert.Equal(null, tableAttr.RowKey);
            Assert.Equal("f1", tableAttr.Filter);
        }

        [Fact]
        public void DefaultTypeForTable()
        {
            JobHostConfiguration config = TestHelpers.NewConfig();
            var metadataProvider = config.CreateMetadataProvider();

            var t1 = metadataProvider.GetDefaultType(new TableAttribute("table1"), FileAccess.Read, null);
            Assert.Equal(typeof(JArray), t1);

            var t2 = metadataProvider.GetDefaultType(new TableAttribute("table1", "pk", "rk"), FileAccess.Read, null);
            Assert.Equal(typeof(JObject), t2);

            var t3 = metadataProvider.GetDefaultType(new TableAttribute("table1"), FileAccess.Write, null);
            Assert.Equal(typeof(IAsyncCollector<JObject>), t3);
        }


        [Fact]
        public void DefaultTypeForQueue()
        {
            JobHostConfiguration config = TestHelpers.NewConfig();
            var metadataProvider = config.CreateMetadataProvider();

            var t1 = metadataProvider.GetDefaultType(new QueueAttribute("q"), FileAccess.Read, typeof(byte[]));
            Assert.Equal(typeof(byte[]), t1);

            var t2 = metadataProvider.GetDefaultType(new QueueAttribute("q"), FileAccess.Read, null);
            Assert.Equal(typeof(string), t2);
                        
            var t3 = metadataProvider.GetDefaultType(new QueueAttribute("q"), FileAccess.Write, null);
            Assert.Equal(typeof(IAsyncCollector<byte[]>), t3);
        }

        [Binding]
        public class TestAttribute : Attribute
        {
            public TestAttribute(string flag)
            {
                this.Flag = flag;
            }
            public string Flag { get; set; }
        }

        public class Widget
        {
            public string Value;
        }

        public class TestExtension : IExtensionConfigProvider            
        {
            public int _counter; 

            public void Initialize(ExtensionConfigContext context)
            {
                _counter++;
                context.AddBindingRule<TestAttribute>().
                    BindToInput<Widget>(Builder);

                context.AddConverter<Widget, JObject>(widget => JObject.FromObject(widget));                
            }

            Widget Builder(TestAttribute input)
            {
                return new Widget { Value = input.Flag };
            }
        }

        public class MyProg
        {
            public string _value;
            public void Test([Test("f1")] Widget w)
            {
                _value = w.Value;
            }
        }
    }
}
