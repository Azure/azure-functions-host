// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Bindings;
using System.Threading;
using Newtonsoft.Json;
using Microsoft.Azure.WebJobs.Description;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Common
{
    // Test BindingFactory's BindToInput rule.
    // Provide some basic types, converters, builders and make it very easy to test a
    // variety of configuration permutations. 
    // Each Client configuration is its own test case. 
    public class BindToGenericItemTests
    {
        // Each of the TestConfigs below implement this. 
        interface ITest<TConfig>
        {
            void Test(TestJobHost<TConfig> host);
        }

        // Simple case. 
        // Test with concrete types, no converters.
        // Attr-->Widget 
        [Fact]
        public void TestConcreteTypeNoConverter()
        {
            TestWorker<ConfigConcreteTypeNoConverter>();
        }
        
        public class ConfigConcreteTypeNoConverter : IExtensionConfigProvider, ITest<ConfigConcreteTypeNoConverter>
        {
            public void Initialize(ExtensionConfigContext context)
            {
                var bf = context.Config.BindingFactory;
                var rule = bf.BindToInput<TestAttribute, AlphaType>(typeof(AlphaBuilder));
                context.RegisterBindingRules<TestAttribute>(rule);
            }

            public void Test(TestJobHost<ConfigConcreteTypeNoConverter> host)
            {
                host.Call("Func", new { k = 1 });
                Assert.Equal("AlphaBuilder(1)", _log);
            }

            string _log;

            // Input Rule (exact match): --> Widget 
            public void Func([Test("{k}")] AlphaType w)
            {
                _log = w._value;
            }         
        }

        // Simple end-2-end case with attribute that default binds to metho dname 
        // Test with concrete types, no converters.
        // Attr-->Widget 
        [Fact]
        public void TestDefaultToMethodName()
        {
            TestWorker<ConfigTestDefaultToMethodName>();
        }

        public class ConfigTestDefaultToMethodName : IExtensionConfigProvider, ITest<ConfigTestDefaultToMethodName>
        {
            public void Initialize(ExtensionConfigContext context)
            {
                var rule = context.AddBindingRule<Test2Attribute>();
                rule.BindToInput<string>(attr => attr.Path);               
            }

            public void Test(TestJobHost<ConfigTestDefaultToMethodName> host)
            {
                host.Call("Func", new { k = 1 });
                Assert.NotNull(_log);

                host.Call("Func2", new { k = 1 });
                Assert.Equal("Func2", _log);
            }

            string _log;

            public void Func([Test2(Path = "{k}*{sys.randGuid:N}*{sys.randGuid:B}*{sys.UtcNow:yyyy}")] string w)
            {
                var parts = w.Split('*');
                string k = parts[0];
                Assert.Equal("1", k);

                string guidstr1 = parts[1];
                var guid1 = Guid.Parse(guidstr1);
                string guidstr2 = parts[2];
                var guid2 = Guid.Parse(guidstr2);                

                Assert.Equal(guid1.ToString("N"), guidstr1);
                Assert.Equal(guid2.ToString("B"), guidstr2);
                Assert.NotEqual(guid1, guid2); // each sys.RandGuid is a different value 

                string date = parts[3];
                Assert.Equal(DateTime.UtcNow.Year.ToString(), date);

                _log = w;
            }

            // Missing path, will default to method name 
            public void Func2([Test2] string w)
            {
                _log = w;
            }
        }

        // Use OpenType (a general builder), still no converters. 
        [Fact]
        public void TestOpenTypeNoConverters()
        {
            TestWorker<ConfigOpenTypeNoConverters>();
        }
   
        public class ConfigOpenTypeNoConverters : IExtensionConfigProvider, ITest<ConfigOpenTypeNoConverters>
        {
            public void Initialize(ExtensionConfigContext context)
            {
                var bf = context.Config.BindingFactory;

                // Replaces BindToGeneric
                var rule = bf.BindToInput<TestAttribute, OpenType>(typeof(GeneralBuilder<>));
                                
                context.RegisterBindingRules<TestAttribute>(rule);
            }
            
            public void Test(TestJobHost<ConfigOpenTypeNoConverters> host)
            {
                host.Call("Func1", new { k = 1 });
                Assert.Equal("GeneralBuilder_AlphaType(1)", _log); 

                host.Call("Func2", new { k = 2 });
                Assert.Equal("GeneralBuilder_BetaType(2)", _log);
            }

            string _log;

            // Input Rule (generic match): --> Widget
            public void Func1([Test("{k}")] AlphaType w)
            {
                _log = w._value;
            }

            // Input Rule (generic match): --> OtherType
            public void Func2([Test("{k}")] BetaType w)
            {
                _log = w._value;
            }
        }

        [Fact]
        public void TestWithConverters()
        {
            TestWorker<ConfigWithConverters>();
        }

        public class ConfigWithConverters : IExtensionConfigProvider, ITest<ConfigWithConverters>
        {
            public void Initialize(ExtensionConfigContext context)
            {
                var bf = context.Config.BindingFactory;

                bf.ConverterManager.AddConverter<AlphaType, BetaType>(ConvertAlpha2Beta);
                
                // The AlphaType restriction here means that although we have a GeneralBuilder<> that *could*
                // directly build a BetaType, we can only use it to build AlphaTypes, and so we must invoke the converter.
                var rule = bf.BindToInput<TestAttribute, AlphaType>(typeof(GeneralBuilder<>));
                                
                context.RegisterBindingRules<TestAttribute>(rule);
            }

            public void Test(TestJobHost<ConfigWithConverters> host)
            {
                host.Call("Func1", new { k = 1 });
                Assert.Equal("GeneralBuilder_AlphaType(1)", _log);

                host.Call("Func2", new { k = 2 });
                Assert.Equal("A2B(GeneralBuilder_AlphaType(2))", _log);                
            }

            string _log;

            // Input Rule (exact match):  --> Widget
            public void Func1([Test("{k}")] AlphaType w)
            {
                _log = w._value;
            }

            // Input Rule (match w/ converter) : --> Widget
            // Converter: Widget --> OtherType
            public void Func2([Test("{k}")] BetaType w)
            {
                _log = w._value;
            }
        }

        // Test ordering. First rule wins. 
        [Fact]
        public void TestMultipleRules()
        {
            TestWorker<ConfigConcreteTypeNoConverter>();
        }

        public class ConfigMultipleRules : IExtensionConfigProvider, ITest<ConfigMultipleRules>
        {
            public void Initialize(ExtensionConfigContext context)
            {
                var bf = context.Config.BindingFactory;
                var rule1 = bf.BindToInput<TestAttribute, AlphaType>(typeof(AlphaBuilder));
                var rule2 = bf.BindToInput<TestAttribute, BetaType>(typeof(BetaBuilder));
                context.RegisterBindingRules<TestAttribute>(rule1, rule2);
            }

            public void Test(TestJobHost<ConfigMultipleRules> host)
            {
                host.Call("Func", new { k = 1 });
                Assert.Equal("AlphaBuilder(1)", _log);

                host.Call("Func2", new { k = 1 });
                Assert.Equal("BetaBuilder(1)", _log);
            }

            string _log;
                        
            public void Func([Test("{k}")] AlphaType w)
            {
                _log = w._value;
            }

            // Input Rule (exact match): --> Widget 
            public void Func2([Test("{k}")] BetaType w)
            {
                _log = w._value;
            }
        }

        // Test binding to object with an explicit converter. 
        [Fact]
        public void TestExplicitObjectConverter()
        {
            TestWorker<ConfigExplicitObjectConverter>();
        }

        public class ConfigExplicitObjectConverter : IExtensionConfigProvider, ITest<ConfigExplicitObjectConverter>, 
            IConverter<AlphaType, object>
        {
            public void Initialize(ExtensionConfigContext context)
            {
                var bf = context.Config.BindingFactory;

                var cm = bf.ConverterManager;
                // Have an explicit converter to object. 
                cm.AddConverter<AlphaType, object, TestAttribute>(this);

                var rule1 = bf.BindToInput<TestAttribute, AlphaType>(typeof(GeneralBuilder<>));
                context.RegisterBindingRules<TestAttribute>(rule1);
            }

            public void Test(TestJobHost<ConfigExplicitObjectConverter> host)
            {
                // normal case
                host.Call("Func", new { k = 1 });
                Assert.Equal("GeneralBuilder_AlphaType(1)", _log);

                // use 1st rule with explicit converter
                host.Call("FuncObject", new { k = 1 });
                Assert.Equal("Alpha2Obj(GeneralBuilder_AlphaType(1))", _log);
            }

            string _log;

            // builds AlphaDerivedType, and then applies an implicit inheritence converter.
            public void Func([Test("{k}")] AlphaType w)
            {
                _log = w._value;
            }

            // Invokes a converter 
            public void FuncObject([Test("{k}")] object w)
            {                
                _log = w.ToString();
            }

            object IConverter<AlphaType, object>.Convert(AlphaType input)
            {
                return $"Alpha2Obj({input._value})"; 
            }
        }

        // Test binding to object. 
        [Fact]
        public void TestObjectInheritence()
        {
            TestWorker<ConfigObjectInheritence>();
        }

        public class ConfigObjectInheritence : IExtensionConfigProvider, ITest<ConfigObjectInheritence>, IConverter<TestAttribute, object>
        {
            public void Initialize(ExtensionConfigContext context)
            {
                var bf = context.Config.BindingFactory;
                // No explicit converters -  just use implicit ones like inheritence
                var rule1 = bf.BindToInput<TestAttribute, AlphaDerivedType>(typeof(GeneralBuilder<>));
                var rule2 = bf.BindToInput<TestAttribute, object>(this); // 2nd rule
                context.RegisterBindingRules<TestAttribute>(rule1, rule2);
            }

            public void Test(TestJobHost<ConfigObjectInheritence> host)
            {
                // 1st rule
                host.Call("FuncDerived", new { k = 1 });
                Assert.Equal("GeneralBuilder_AlphaDerivedType(1)", _log);

                // 1st rule + implicit converter
                host.Call("Func", new { k = 1 });
                Assert.Equal("GeneralBuilder_AlphaDerivedType(1)", _log);

                // 2nd rule, object isn't matched in an inheritence converter
                host.Call("FuncObject", new { k = 1 });
                Assert.Equal("[obj!]", _log);
            }

            string _log;

            public void FuncDerived([Test("{k}")] AlphaDerivedType w)
            {                
                _log = w._value;
            }

            // builds AlphaDerivedType, and then applies an implicit inheritence converter.
            public void Func([Test("{k}")] AlphaType w)
            {
                // Actually passed in a derived instance
                Assert.IsType<AlphaDerivedType>(w);

                _log = w._value;
            }

            // Uses the direct -->object binding rule 
            public void FuncObject([Test("{k}")] object w)
            {
                var beta = Assert.IsType<BetaType>(w);
                _log = beta._value;
            }         

            public object Convert(TestAttribute input)
            {
                return BetaType.New("[obj!]");
            }
        }

        // Test collectors and object[] bindings. 
        // Object[] --> multiple items 
        [Fact]
        public void TestConfigCollectorMultipleItems()
        {
            TestWorker<ConfigCollector<NonArrayOpenType>>();
        }

        // Test collectors and object[] bindings. 
        // Object[] --> single item
        [Fact]
        public void TestConfigCollectorSingleItem()
        {
            TestWorker<ConfigCollector<OpenType>>();
        }

        public class ConfigCollector<TParam> : 
            IExtensionConfigProvider, 
            ITest<ConfigCollector<TParam>>,
            IConverter<TestAttribute, IAsyncCollector<AlphaType>>
        {        
            public string _log;

            public IAsyncCollector<AlphaType> Convert(TestAttribute arg)
            {
                return new AlphaTypeCollector { _parent = this };
            }
            
            public class Object2AlphaConverter : IConverter<object, AlphaType>
            {
                public AlphaType Convert(object obj)
                {
                    var json = JsonConvert.SerializeObject(obj);
                    return AlphaType.New($"Json({json})");
                }
            }

            class AlphaTypeCollector : IAsyncCollector<AlphaType>
            {
                public ConfigCollector<TParam> _parent;

                public Task AddAsync(AlphaType item, CancellationToken cancellationToken = default(CancellationToken))
                {
                    _parent._log += $"Collector({item._value});";
                    return Task.FromResult(0);
                }

                public Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
                {
                    return Task.FromResult(0);
                }
            }
      
            public void Initialize(ExtensionConfigContext context)
            {
                var bf = context.Config.BindingFactory;

                // The converter rule is the key switch.
                // If TParam==SingleType, then that means we can only convert from non-array types to AlphaType.
                //  that means object[] converts to AlphaType[]  (many)
                // If TParam==OpenType, then we can convert any type (including arrays) to an AlphaType.
                //  that means object[] converts to AlphaType (one) 
                bf.ConverterManager.AddConverter<TParam, AlphaType, TestAttribute>(typeof(Object2AlphaConverter));

                var rule1 = bf.BindToCollector<TestAttribute, AlphaType>(this);
                context.RegisterBindingRules<TestAttribute>(rule1);
            }

            public void Test(TestJobHost<ConfigCollector<TParam>> host)
            {
                // tells you we made 2 AddAysnc calls, and invoked the converter on each item. 
                _log = "";
                host.Call("Func2", new { k = 1 });

                if (typeof(TParam) == typeof(NonArrayOpenType))
                {
                    // Each object gets converter, so object[] gets converterd to multiple types. 
                    Assert.Equal("Collector(Json(123));Collector(Json(\"xyz\"));", _log);
                }
                else
                {
                    // the object[] gets converters to a single element to a single object
                    Assert.Equal("Collector(Json([123,\"xyz\"]));", _log);                    
                }

                // 2 calls, but no converters
                _log = "";
                host.Call("Func", new { k = 1 });
                Assert.Equal("Collector(v1);Collector(v2);", _log); 
            }
                        
            public async Task Func([Test("{k}")] IAsyncCollector<AlphaType> collector)
            {
                await collector.AddAsync(AlphaType.New("v1"));
                await collector.AddAsync(AlphaType.New("v2"));
            }
                        
            public void Func2([Test("{k}")] out object[] foo)
            {
                foo = new object[] {
                    123,
                    "xyz"
                };
            }
        }

        // Matches to 'object' but not 'object[]'
        public class NonArrayOpenType : OpenType
        {
            public override bool IsMatch(Type type)
            {
                return !type.IsArray;
            }
        }

        // Error case. 
        [Fact]
        public void TestError1()
        {
            // Test an error in configuration setup. This is targeted at the extension author.
            TestWorker<ConfigError1>();
        }

        public class ConfigError1 : IExtensionConfigProvider, ITest<ConfigError1>
        {
            public void Initialize(ExtensionConfigContext context)
            {
                var bf = context.Config.BindingFactory;
                
                // This is an error. The rule specifies OpenType,which allows any type.
                // But the builder can only produce Alpha types. 
                var rule = bf.BindToInput<TestAttribute, OpenType>(typeof(AlphaBuilder));

                context.RegisterBindingRules<TestAttribute>(rule);
            }

            public void Test(TestJobHost<ConfigError1> host)
            {
                host.AssertIndexingError("Func", "No Convert method on type AlphaBuilder to convert from TestAttribute to BetaType");
            }
      
            // Fail to bind because: 
            // We only have an AlphaBuilder, and no registered converters from Alpha-->Beta
            public void Func([Test("{k}")] BetaType w)
            {
                Assert.False(true); // Method shouldn't have been invoked. 
            }
        }

        // Error case: verify that we don't do an arbitrary depth search
        [Fact]
        public void TestErrorSearch()
        {
            TestWorker<ConfigErrorSearch>();
        }

        public class ConfigErrorSearch : IExtensionConfigProvider, ITest<ConfigErrorSearch>
        {
            public void Initialize(ExtensionConfigContext context)
            {
                var bf = context.Config.BindingFactory;

                var cm = bf.ConverterManager;
                cm.AddConverter<AlphaType, BetaType>(ConvertAlpha2Beta);
                cm.AddConverter<BetaType, string>((beta) => $"Str({beta._value})" );
                var rule = bf.BindToInput<TestAttribute, AlphaType>(typeof(AlphaBuilder));

                context.RegisterBindingRules<TestAttribute>(rule);
            }

            public void Test(TestJobHost<ConfigErrorSearch> host)
            {
                host.AssertIndexingError("Func", "Can't bind Test to type 'System.String'.");
            }

            // Fail to bind because: 
            // We don't chain multiple converters together. 
            // So we don't do TestAttr --> Alpha --> Beta --> string
            public void Func([Test("{k}")] string w)
            {
                Assert.False(true); // Method shouldn't have been invoked. 
            }
        }

        // Get standard error message for failing to bind an attribute to a given parameter type.
        static string ErrorMessage(Type parameterType)
        {
            return $"Can't bind Test to type '{parameterType.FullName}'.";
        }
     
        // Glue to initialize a JobHost with the correct config and invoke the Test method. 
        // Config also has the program on it.         
        private void TestWorker<TConfig>() where TConfig : IExtensionConfigProvider, ITest<TConfig>, new() 
        {
            var prog = new TConfig();
            var jobActivator = new FakeActivator();
            jobActivator.Add(prog);

            IExtensionConfigProvider ext = prog;
            var host = TestHelpers.NewJobHost<TConfig>(jobActivator, ext);

            ITest<TConfig> test = prog;
            test.Test(host);
        }
                
        // Some custom type to bind to. 
        public class AlphaType
        {
            public static AlphaType New(string value)
            {
                return new AlphaType { _value = value };
            }

            public string _value;
        }

        // Some custom type to bind to. 
        public class AlphaDerivedType : AlphaType
        {
            public static new AlphaDerivedType New(string value)
            {
                return new AlphaDerivedType { _value = value };
            }            
        }


        // Another custom type, not related to the first type. 
        public class BetaType
        {
            public static BetaType New(string value)
            {
                return new BetaType { _value = value };
            }

            public string _value;
        }

        static BetaType ConvertAlpha2Beta(AlphaType x)
        {
            return BetaType.New($"A2B({x._value})");
        }

        // A test attribute for binding.  
        public class TestAttribute : Attribute
        {
            public TestAttribute(string path)
            {
                this.Path = path;
            }

            [AutoResolve]
            public string Path { get; set; }
        }

        // A test attribute for binding.  
        [Binding]
        public class Test2Attribute : Attribute
        {
            [AutoResolve(Default = "{sys.methodname}")]
            public string Path { get; set; }
        }

        // Converter for building instances of RedType from an attribute
        class AlphaBuilder : IConverter<TestAttribute, AlphaType>
        {
            // Test explicit interface implementation 
            AlphaType IConverter<TestAttribute, AlphaType>.Convert(TestAttribute attr)
            {
                return AlphaType.New("AlphaBuilder(" + attr.Path + ")");
            }
        }

        // Converter for building instances of RedType from an attribute
        class BetaBuilder : IConverter<TestAttribute, BetaType>
        {
            // Test with implicit interface implementation 
            public BetaType Convert(TestAttribute attr)
            {
                return BetaType.New("BetaBuilder(" + attr.Path + ")");
            }
        }

        // Can build Widgets or OtherType
        class GeneralBuilder<T> : IConverter<TestAttribute, T>
        {
            private readonly MethodInfo _builder;

            public GeneralBuilder()
            {
                _builder = typeof(T).GetMethod("New", BindingFlags.Public | BindingFlags.Static);
                if (_builder == null)
                {
                    throw new InvalidOperationException($"Type  {typeof(T).Name} should have a static New() method");
                }
            }

            public T Convert(TestAttribute attr)
            {
                var value = $"GeneralBuilder_{typeof(T).Name}({attr.Path})";
                return (T)_builder.Invoke(null, new object[] { value});
            }
        }
    }
}
