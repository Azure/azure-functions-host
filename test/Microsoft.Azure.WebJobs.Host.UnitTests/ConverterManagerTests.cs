// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Xunit;
using System.Text;
using Newtonsoft.Json;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Reflection;
using System.Collections.Generic;

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
            // strip whitespace
            string val = Regex.Replace(x1.Value, @"\s", "");
            string expected = String.Format("{{\"Value2\":\"abc\",\"$\":\"{0}\"}}", instance);

            Assert.Equal(expected, val);
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


        // Test converter using Open generic types
        class TypeConverterWithTwoGenericArgs<TInput, TOutput> :
            IConverter<TInput, TOutput>
        {
            public TypeConverterWithTwoGenericArgs(ConverterManagerTests config)
            { 
                // We know this is only used by a single test invoking with this combination of params.
                Assert.Equal(typeof(String), typeof(TInput));
                Assert.Equal(typeof(int), typeof(TOutput));

                config._counter++;
            }

            public TOutput Convert(TInput input)
            {
                var str = (string)(object)input;
                return (TOutput)(object)int.Parse(str);
            }
        }

        [Fact]
        public void OpenTypeConverterWithTwoGenericArgs()
        {
            Assert.Equal(0, _counter);
            var cm = new ConverterManager();

            // Register a converter builder. 
            // Builder runs once; converter runs each time.
            // Uses open type to match. 
            cm.AddConverter<TypeWrapperIsString, int, Attribute>(typeof(TypeConverterWithTwoGenericArgs<,>), this);

            var converter = cm.GetConverter<string, int, Attribute>();

            Assert.Equal(12, converter("12", new TestAttribute(null), null));
            Assert.Equal(34, converter("34", new TestAttribute(null), null));

            Assert.Equal(1, _counter); // converterBuilder is only called once. 

            // 'char' as src parameter doesn't match the type predicate. 
            Assert.Null(cm.GetConverter<char, int, Attribute>());
        }
               

        // Test converter using Open generic types, rearranging generics
        class TypeConverterWithOneGenericArg<TElement> : 
            IConverter<TElement, IEnumerable<TElement>>
        {
            public IEnumerable<TElement> Convert(TElement input)
            {
                // Trivial rule. 
                return new TElement[] { input, input, input };
            }
        }

        [Fact]
        public void OpenTypeConverterWithOneGenericArg()
        {
            var cm = new ConverterManager();

            // Register a converter builder. 
            // Builder runs once; converter runs each time.
            // Uses open type to match. 
            // Also test the IEnumerable<OpenType> pattern. 
            cm.AddConverter<OpenType, IEnumerable<OpenType>, Attribute>(typeof(TypeConverterWithOneGenericArg<>));

            var attr = new TestAttribute(null);

            {
                var converter = cm.GetConverter<int, IEnumerable<int>, Attribute>();
                Assert.Equal(new int[] { 1, 1, 1 }, converter(1, attr, null));
            }

            {
                var converter = cm.GetConverter<string, IEnumerable<string>, Attribute>();
                Assert.Equal(new string[] { "a", "a", "a" }, converter("a", attr, null));
            }
        }

        // Counter used by tests to verify that converter ctors are only run once and then shared across 
        // multiple invocations. 
        private int _counter;

        class ConverterInstanceMethod :
            IConverter<string, int>
        {
            // Converter discovered for OpenType4 test. Used directly. 
            public int Convert(string input)
            {
                return int.Parse(input);
            }
        }

        [Fact]
        public void OpenTypeSimpleConcreteConverter()
        {
            Assert.Equal(0, _counter);
            var cm = new ConverterManager();

            // Register a converter builder. 
            // Builder runs once; converter runs each time.
            // Uses open type to match. 
            cm.AddConverter<TypeWrapperIsString, int, Attribute>(new ConverterInstanceMethod());

            var converter = cm.GetConverter<string, int, Attribute>();

            Assert.Equal(12, converter("12", new TestAttribute(null), null));
            Assert.Equal(34, converter("34", new TestAttribute(null), null));

            Assert.Equal(0, _counter); // passed in instantiated object; counter never incremented. 

            // 'char' as src parameter doesn't match the type predicate. 
            Assert.Null(cm.GetConverter<char, int, Attribute>());
        }


        // Test converter using concrete types. 
        class TypeConverterWithConcreteTypes
            : IConverter<string, int>
        {
            public TypeConverterWithConcreteTypes(ConverterManagerTests config)
            {
                config._counter++;
            }

            public int Convert(string input)
            {
                return int.Parse(input);
            }
        }

        [Fact]
        public void OpenTypeConverterWithConcreteTypes()
        {
            Assert.Equal(0, _counter);
            var cm = new ConverterManager();

            // Register a converter builder. 
            // Builder runs once; converter runs each time.
            // Uses open type to match. 
            cm.AddConverter<TypeWrapperIsString, int, Attribute>(typeof(TypeConverterWithConcreteTypes), this);

            var converter = cm.GetConverter<string, int, Attribute>();

            Assert.Equal(12, converter("12", new TestAttribute(null), null));
            Assert.Equal(34, converter("34", new TestAttribute(null), null));

            Assert.Equal(1, _counter); // converterBuilder is only called once. 

            // 'char' as src parameter doesn't match the type predicate. 
            Assert.Null(cm.GetConverter<char, int, Attribute>());
        }

        [Fact]
        public void OpenType()
        {
            int count = 0;
            var cm = new ConverterManager();

            // Register a converter builder. 
            // Builder runs once; converter runs each time.
            // Uses open type to match. 
            cm.AddConverter<TypeWrapperIsString, int, Attribute>(
                (typeSrc, typeDest) =>
                {
                    count++;
                    Assert.Equal(typeof(String), typeSrc);
                    Assert.Equal(typeof(int), typeDest);

                    return (input) =>
                    {
                        string s = (string)input;
                        return int.Parse(s);
                    };
                });

            var converter = cm.GetConverter<string, int, Attribute>();
            Assert.NotNull(converter);
            Assert.Equal(12, converter("12", new TestAttribute(null), null));
            Assert.Equal(34, converter("34", new TestAttribute(null), null));            

            Assert.Equal(1, count); // converterBuilder is only called once. 

            // 'char' as src parameter doesn't match the type predicate. 
            Assert.Null(cm.GetConverter<char, int, Attribute>());
        }


        // Test with async converter 
        public class UseAsyncConverter : IAsyncConverter<int, string>
        {
            public Task<string> ConvertAsync(int i, CancellationToken cancellationToken)
            {
                return Task.FromResult(i.ToString());
            }
        }

        // Test non-generic Task<string>, use instance match. 
        [Fact]
        public void UseUseAsyncConverterTest()
        {
            var cm = new ConverterManager();

            cm.AddConverter<int, string, Attribute>(new UseAsyncConverter());

            var converter = cm.GetConverter<int, string, Attribute>();

            Assert.Equal("12", converter(12, new TestAttribute(null), null));            
        }

        // Test with async converter 
        public class UseGenericAsyncConverter<T> :
            IAsyncConverter<int, T>
        {
            public Task<T> ConvertAsync(int i, CancellationToken token)
            {
                Assert.Equal(typeof(string), typeof(T));
                return Task.FromResult((T) (object) i.ToString());
            }
        }

        // Test generic Task<T>, use typing match. 
        [Fact]
        public void UseGenericAsyncConverterTest()
        {
            var cm = new ConverterManager();

            cm.AddConverter<int, string, Attribute>(typeof(UseGenericAsyncConverter<>));

            var converter = cm.GetConverter<int, string, Attribute>();

            Assert.Equal("12", converter(12, new TestAttribute(null), null));
        }

        // Sample types to excercise pattern matcher
        class Foo<T1, T2> : IConverter<T1, IDictionary<char, T2>>
        {
            public IDictionary<char, T2> Convert(T1 input)
            {
                throw new NotImplementedException();
            }
        }

        // Unit tests for TestPatternMatcher.ResolveGenerics
        [Fact]
        public void TestPatternMatcher_ResolveGenerics()
        {
            var typeFoo = typeof(Foo<,>);
            var int1 = typeFoo.GetInterfaces()[0]; // IConverter<T1, IDictionary<T1, T2>>
            var typeFoo_T1 = typeFoo.GetGenericArguments()[0];
            var typeIConverter_T1 = int1.GetGenericArguments()[0];
            var typeIConverter_IDictChar_T2 = int1.GetGenericArguments()[1];

            var genArgs = new Dictionary<string, Type>
            {
                { "T1", typeof(int) },
                { "T2", typeof(string) }
            };

            Assert.Equal(typeof(int), PatternMatcher.ResolveGenerics(typeof(int), genArgs));

            var typeFooIntStr = typeof(Foo<int, string>);
            Assert.Equal(typeFooIntStr, PatternMatcher.ResolveGenerics(typeFooIntStr, genArgs));

            Assert.Equal(typeof(int), PatternMatcher.ResolveGenerics(typeFoo_T1, genArgs));
            Assert.Equal(typeof(int), PatternMatcher.ResolveGenerics(typeIConverter_T1, genArgs));
            Assert.Equal(typeof(IDictionary<char,string>), PatternMatcher.ResolveGenerics(typeIConverter_IDictChar_T2, genArgs));

            Assert.Equal(typeof(Foo<int, string>), PatternMatcher.ResolveGenerics(typeFoo, genArgs));

            Assert.Equal(typeof(IConverter<int, IDictionary<char, string>>), 
                PatternMatcher.ResolveGenerics(int1, genArgs));
        }

        private class TestConverterFakeEntity
            : IConverter<JObject, IFakeEntity>
        {
            public IFakeEntity Convert(JObject obj)
            {
                return new MyFakeEntity { Property = obj["Property1"].ToString() };
            }
        }

        private class TestConverterFakeEntity<T>
            : IConverter<T, IFakeEntity>
        {
            public IFakeEntity Convert(T item)
            {
                Assert.IsType<PocoEntity>(item); // test only calls this with PocoEntity 
                var d = (PocoEntity) (object) item;
                string propValue = d.Property2;
                return new MyFakeEntity { Property = propValue };
            }
        }

        // Test sort of rules that we have in tables. 
        // Rules can overlap, so make sure that the right rule is dispatched. 
        // Poco is a base class of  Jobject and IFakeEntity.
        // Give each rule its own unique converter and ensure each converter is called.  
        [Fact]
        public void TestConvertFakeEntity()
        {
            var cm = new ConverterManager();

            // Derived<ITableEntity> --> IFakeEntity  [automatic] 
            // JObject --> IFakeEntity
            // Poco --> IFakeEntity
            cm.AddConverter<JObject, IFakeEntity, Attribute>(typeof(TestConverterFakeEntity));
            cm.AddConverter<OpenType, IFakeEntity, Attribute>(typeof(TestConverterFakeEntity<>));

            {
                var converter = cm.GetConverter<IFakeEntity, IFakeEntity, Attribute>();
                var src = new MyFakeEntity { Property = "123" };
                var dest = converter(src, null, null);
                Assert.Same(src, dest); // should be exact same instance - no conversion 
            }

            {
                var converter = cm.GetConverter<JObject, IFakeEntity, Attribute>();
                JObject obj = new JObject();
                obj["Property1"] = "456";
                var dest = converter(obj, null, null);
                Assert.Equal("456", dest.Property);
            }

            {
                var converter = cm.GetConverter<PocoEntity, IFakeEntity, Attribute>();
                var src = new PocoEntity { Property2 = "789" };                
                var dest = converter(src, null, null);
                Assert.Equal("789", dest.Property);
            }
        }

        // Class that implements IFakeEntity. Test conversions.  
        class MyFakeEntity : IFakeEntity
        {
            public string Property { get; set; }
        }

        interface IFakeEntity
        {
            string Property { get; }
        }

        // Poco class that can be converted to IFakeEntity, but doesn't actually implement IFakeEntity. 
        class PocoEntity
        {
            // Give a different property name so that we can enforce the exact converter.
            public string Property2 { get; set; }
        }

        class TypeWrapperIsString : OpenType
        {
            // Predicate is invoked by ConverterManager to determine if a type matches. 
            public override bool IsMatch(Type t)
            {
                return t == typeof(string);
            }
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