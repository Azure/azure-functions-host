// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Xunit;
using System.Text;
using Newtonsoft.Json;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Newtonsoft.Json.Linq;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class ConverterManagerTests
    {
        static ValueBindingContext Context = null;

        // Can always convert a type to itself. 
        [Fact]
        public void Identity()
        {
            var cm = new ConverterManager(); // empty 

            var identity = cm.GetConverter<string, string, Attribute>();

            var value = "abc";
            var x1 = identity(value, null, Context);
            Assert.Same(x1, value);
        }

        // Explicit converters take precedence. 
        [Fact]
        public void ExactMatchOverride()
        {
            var cm = new ConverterManager(); // empty 
            cm.AddConverter<string, string>(x => "*" + x + "*");

            var func = cm.GetConverter<string, string, Attribute>();
                        
            var x1 = func("x", null, Context);
            Assert.Equal("*x*", x1);
        }

        // Use a value binding context to stamp causality on a JObject        
        // This is what [Queue] does. 
        [Fact]
        public void UseValueBindingContext()
        {
            var cm = new ConverterManager(); // empty 

            Guid instance = Guid.NewGuid();
            var testContext = new ValueBindingContext(new FunctionBindingContext(instance, CancellationToken.None, null), CancellationToken.None);

            cm.AddConverter((object obj, Attribute attr, ValueBindingContext ctx) => {
                Assert.Same(ctx, testContext);
                var result = JObject.FromObject(obj);
                result["$"] = ctx.FunctionInstanceId;
                return result;
            });
            cm.AddConverter<string, Wrapper>(str => new Wrapper { Value = str });

            // Expected: 
            //    Other --> JObject,  
            //    JObject --> string ,  (builtin) 
            //    string --> Wrapper
            var func = cm.GetConverter<Other, Wrapper, Attribute>();

            var value = new Other { Value2 = "abc" };
            Wrapper x1 = func(value, null, testContext);

            Assert.Equal(@"{
  ""Value2"": ""abc"",
  ""$"": """ + instance.ToString() + @"""
}", x1.Value);

    }

        // Explicit converters take precedence. 
        [Fact]
        public void Inheritence()
        {
            var cm = new ConverterManager(); // empty             
            var func = cm.GetConverter<DerivedWrapper, Wrapper, Attribute>();

            var obj = new DerivedWrapper { Value = "x" };
            Wrapper x1 = func(obj, null, Context);
            Assert.Same(x1, obj);
        }

        // Object is a catch-all
        [Fact]
        public void CatchAll()
        {
            var cm = new ConverterManager(); // empty 
            cm.AddConverter<object, Wrapper>(x => new Wrapper { Value = x.ToString() });

            var func = cm.GetConverter<int, Wrapper, Attribute>();

            var x1 = func(123, null, Context);
            Assert.Equal("123", x1.Value);
        }

        // Byte[] and String converters. 
        [Fact]
        public void StringAndByteArray()
        {
            var cm = new ConverterManager(); // empty             

            // No default byte[]-->Wrapper conversion. 
            var fromBytes = cm.GetConverter<byte[], Wrapper, Attribute>();
            Assert.Null(fromBytes);

            // Add a string-->Wrapper conversion
            cm.AddConverter<string, Wrapper>(str => new Wrapper { Value = str });

            var fromString = cm.GetConverter<string, Wrapper, Attribute>();
            Wrapper obj1 = fromString("abc", null, Context);
            Assert.Equal("abc", obj1.Value);

            // Now we can get a byte-->string  , composed from a default (byte[]-->string) + supplied (string-->Wrapper)
            byte[] bytes = Encoding.UTF8.GetBytes("abc");

            fromBytes = cm.GetConverter<byte[], Wrapper, Attribute>();
            Assert.NotNull(fromBytes);
            Wrapper obj2 = fromBytes(bytes, null, Context);
            Assert.Equal("abc", obj2.Value);

            // Now override the default. Uppercase the string so we know it used our custom converter.
            cm.AddConverter<byte[], string>(b => Encoding.UTF8.GetString(b).ToUpper());
            fromBytes = cm.GetConverter<byte[], Wrapper, Attribute>();
            Wrapper obj3 = fromBytes(bytes, null, Context);
            Assert.Equal("ABC", obj3.Value);
        }

        // Allow Json serialization if we have a String-->T converter 
        [Fact]
        public void JsonSerialization()
        {
            var cm = new ConverterManager(); // empty             
            
            cm.AddConverter<string, Wrapper>(str => new Wrapper { Value = str });

            var objSrc = new Other { Value2 = "abc" };

            // Json Serialize: (Other --> string)
            // custom          (string -->Wrapper)
            var func = cm.GetConverter<Other, Wrapper, Attribute>();
            Wrapper obj2 = func(objSrc, null, Context);

            string json = obj2.Value;
            var objSrc2 = JsonConvert.DeserializeObject<Other>(json);
            Assert.Equal(objSrc.Value2, objSrc2.Value2);            
        }
                
        // Overload conversions on type if they're using different attributes. 
        [Fact]
        public void AttributeOverloads()
        {
            var cm = new ConverterManager(); // empty 
            cm.AddConverter<Wrapper, string, TestAttribute>((x, attr) => string.Format("[t1:{0}-{1}]", x.Value, attr.Flag));
            cm.AddConverter<Wrapper, string, TestAttribute2>((x, attr) => string.Format("[t2:{0}-{1}]", x.Value, attr.Flag));

            // Since converter was registered for a specific attribute, it must be queried by that attribute. 
            var funcMiss = cm.GetConverter<Wrapper, string, Attribute>();
            Assert.Null(funcMiss);

            // Each attribute type has its own conversion function
            var func1 = cm.GetConverter<Wrapper, string, TestAttribute>();
            Assert.NotNull(func1);
            var x1 = func1(new Wrapper { Value = "x" } , new TestAttribute("y"), Context);
            Assert.Equal("[t1:x-y]", x1);

            var func2 = cm.GetConverter<Wrapper, string, TestAttribute2>();
            Assert.NotNull(func2);
            var x2 = func2(new Wrapper { Value = "x" }, new TestAttribute2("y"), Context);
            Assert.Equal("[t2:x-y]", x2);
        }

        // Explicit converters take precedence. 
        [Fact]
        public void AttributeOverloads2()
        {
            var cm = new ConverterManager(); // empty 
            cm.AddConverter<Wrapper, string, TestAttribute>((x, attr) => string.Format("[t1:{0}-{1}]", x.Value, attr.Flag));
            cm.AddConverter<Wrapper, string>(x => string.Format("[common:{0}]", x.Value));
                        
            // This has an exact match on attribute and gives the specific function we registered.
            var func1 = cm.GetConverter<Wrapper, string, TestAttribute>();
            Assert.NotNull(func1);
            var x1 = func1(new Wrapper { Value = "x" }, new TestAttribute("y"), Context);
            Assert.Equal("[t1:x-y]", x1);

            // Nothing registered for this attribute, so we return the converter that didn't require any attribute.
            var func2 = cm.GetConverter<Wrapper, string, TestAttribute2>();
            Assert.NotNull(func2);
            var x2 = func2(new Wrapper { Value = "x" }, new TestAttribute2("y"), Context);
            Assert.Equal("[common:x]", x2);
        }

        // Custom type
        public class Wrapper
        {
            public string Value;
        }

        public class DerivedWrapper : Wrapper
        {
            public int Other;
        }

        // Another custom type, with no relation to Wrapper
        public class Other
        {
            public string Value2;
        }

        public class TestAttribute : Attribute
        {
            public TestAttribute(string flag)
            {
                this.Flag = flag;
            }
            public string Flag { get; set; }
        }

        // Different attribute
        public class TestAttribute2 : Attribute
        {
            public TestAttribute2(string flag)
            {
                this.Flag = flag;
            }
            public string Flag { get; set; }
        }
    }
}