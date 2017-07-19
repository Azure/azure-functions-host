// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Description;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Indexers
{
    public class ReturnValueTests
    {
        [Fact]
        public void ImplicitReturn()
        {
            var ext = new MyExtension();
            var prog = TestHelpers.NewJobHost<TestProg>(ext);

            prog.Call("ImplicitReturn", new { trigger = "trigger" } );
            ext.AssertFromBeta("triggerbeta");        
        }

        [Fact]
        public void ImplicitTaskReturn()
        {
            var ext = new MyExtension();
            var prog = TestHelpers.NewJobHost<TestProg>(ext);

            prog.Call("ImplicitTaskReturn", new { trigger = "trigger" });
            ext.AssertFromBeta("triggerbeta");
        }

        [Fact]
        public void ExplicitReturn()
        {
            var ext = new MyExtension();
            var prog = TestHelpers.NewJobHost<TestProg>(ext);

            prog.Call("ExplicitReturn");
            ext.AssertFromAlpha("alpha");
        }

        [Fact]
        public void ExplicitTaskReturn()
        {
            var ext = new MyExtension();
            var prog = TestHelpers.NewJobHost<TestProg>(ext);

            prog.Call("ExplicitTaskReturn");
            ext.AssertFromAlpha("alpha");
        }

        [Fact]
        public void ExplicitReturnWins()
        {
            var ext = new MyExtension();
            var prog = TestHelpers.NewJobHost<TestProg>(ext);        

            prog.Call("ExplicitReturnWins", new { trigger = "trigger" });
            ext.AssertFromAlpha("triggeralpha");
        }

        [Fact]
        public void TestIndexError()
        {
            var ext = new MyExtension();
            var prog = TestHelpers.NewJobHost<TestProgErrors>(ext);

            TestHelpers.AssertIndexingError(() => prog.Call("Error"), "TestProgErrors.Error",
                "Functions must return Task or void, have a binding attribute for the return value, or be triggered by a binding that natively supports return values.");
        }

        public class TestProgErrors
        {
            // Error to have a return without any binding 
            // Put an attribute on it to ensure it still gets indexed. 
            [NoAutomaticTrigger]
            public string Error()
            {
                return "error";
            }
        }

        // Bind to a regular async collector (output) binding,
        [Binding]
        public class AlphaAttribute : Attribute
        {
        }

        // Bind to a trigger that accepts a return value. 
        [Binding]
        public class BetaAttribute : Attribute
        {
        }

        class MyExtension : IExtensionConfigProvider, IAsyncCollector<string>, ITriggerBindingProvider
        {
            public List<string> _itemsFromAlpha = new List<string>();
            public List<string> _itemsFromBeta = new List<string>();

            public void AssertFromAlpha(string expected)
            {
                Assert.Equal(1, _itemsFromAlpha.Count);
                Assert.Equal(0, _itemsFromBeta.Count);
                Assert.Equal(expected, _itemsFromAlpha[0]);
                _itemsFromAlpha.Clear();
                _itemsFromBeta.Clear();
            }
            public void AssertFromBeta(string expected)
            {
                Assert.Equal(0, _itemsFromAlpha.Count);
                Assert.Equal(1, _itemsFromBeta.Count);
                Assert.Equal(expected, _itemsFromBeta[0]);
                _itemsFromAlpha.Clear();
                _itemsFromBeta.Clear();
            }

            public Task AddAsync(string item, CancellationToken cancellationToken = default(CancellationToken))
            {
                _itemsFromAlpha.Add(item);
                return Task.CompletedTask;
            }

            public Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.CompletedTask;
            }

            public void Initialize(ExtensionConfigContext context)
            {
                context.AddBindingRule<AlphaAttribute>().
                    BindToCollector(attr => this);

                context.AddBindingRule<BetaAttribute>().
                    BindToTrigger(this);
            }
        
            Task<ITriggerBinding> ITriggerBindingProvider.TryCreateAsync(TriggerBindingProviderContext context)
            {
                return Task.FromResult<ITriggerBinding>(new BetaTrigger(this));
            }

            class BetaTrigger : ITriggerBinding
            {
                MyExtension _parent;
                public BetaTrigger(MyExtension parent)
                {
                    _parent = parent;
                }
                public Type TriggerValueType => typeof(string);

                public IReadOnlyDictionary<string, Type> BindingDataContract => new Dictionary<string, Type>
                {
                    { "$return", typeof(string).MakeByRefType() } // Return is same as 'out T'. 
                };

                public Task<ITriggerData> BindAsync(object value, ValueBindingContext context)
                {
                    var inputValueProvider = new BetaValueProvider(_parent) { Value = value };
                    var returnValueProvider = new BetaReturnValueProvider(_parent) { Value = "return" };
                    var bindingData = new Dictionary<string, object>();
                    var triggerData = new TriggerData(inputValueProvider, bindingData)
                    {
                        ReturnValueProvider = returnValueProvider
                    };
                    return Task.FromResult<ITriggerData>(triggerData);
                }

                public Task<IListener> CreateListenerAsync(ListenerFactoryContext context)
                {
                    return Task.FromResult<IListener>(new NullListener());
                }

                public ParameterDescriptor ToParameterDescriptor()
                {
                    return new ParameterDescriptor();
                }
            }

            class BetaValueProvider : IValueProvider
            {
                protected MyExtension _parent;

                public BetaValueProvider(MyExtension parent)
                {
                    _parent = parent;
                }

                public Type Type => typeof(string);

                public object Value;

                public Task<object> GetValueAsync()
                {
                    return Task.FromResult(Value);                    
                }
                public string ToInvokeString()
                {
                    return Value.ToString();
                }
            }

            class BetaReturnValueProvider : BetaValueProvider, IValueBinder
            {
                public BetaReturnValueProvider(MyExtension parent) : base(parent)
                {
                }

                public Task SetValueAsync(object value, CancellationToken cancellationToken)
                {
                    _parent._itemsFromBeta.Add((string) value);
                    return Task.CompletedTask;
                }
            }
        }

        public class TestProg
        {
            // Return is explict
            [return: Alpha]
            public string ExplicitReturn()
            {
                return "alpha";
            }

            // Return is explict
            [return: Alpha]
            public Task<string> ExplicitTaskReturn()
            {
                return Task.FromResult("alpha");
            }

            // Return is from Trigger 
            public string ImplicitReturn([Beta] string trigger)
            {
                return trigger + "beta";
            }

            // Return is from Trigger 
            public Task<string> ImplicitTaskReturn([Beta] string trigger)
            {
                var result = trigger + "beta";
                return Task.FromResult(result);
            }

            [return: Alpha]
            public string ExplicitReturnWins([Beta] string trigger)
            {
                return trigger + "alpha";
            }            
        }
    }
}
