// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Description;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Loggers
{
    // Test that FunctionExecutor emits logs in the right order
    public class FunctionExecutorLogOrdering
    {
        public const string PreBindLog = "[Start]";
        public const string ParamLog = "[Param]";
        public const string ParamFailLog = "[ParamFail]"; // parameter binding failed
        public const string PostBindLog = "[PostBind]";
        public const string BodyLog = "Body";
        public const string BodyFailLog = "[BodyFail]";
        public const string CompletedLog = "[Done]";
        public const string CompletedFailLog = "[DoneFail]";

        // Test that instrumentation points are emitted in the right order.
        [Fact]
        public void TestSuccess()
        {
            var logger = new MyLogger();
            var host = TestHelpers.NewJobHost<MyProg>(logger, new TestExt());
            host.Call("test");

            Assert.Equal(PreBindLog + ParamLog + PostBindLog + BodyLog + CompletedLog, logger._log.ToString());
        }

        [Fact]
        public void TestWithFailParam()
        {
            var logger = new MyLogger();
            var host = TestHelpers.NewJobHost<MyProg>(logger, new TestExt());

            Assert.Throws<FunctionInvocationException>(() => host.Call("testFailParam"));

            Assert.Equal(PreBindLog + ParamFailLog + PostBindLog + CompletedFailLog, logger._log.ToString());
        }

        [Fact]
        public void TestWithFailBody()
        {
            var logger = new MyLogger();
            var host = TestHelpers.NewJobHost<MyProg>(logger, new TestExt());
            Assert.Throws<FunctionInvocationException>(() => host.Call("testFailBody"));

            Assert.Equal(PreBindLog + ParamLog + PostBindLog + BodyFailLog + CompletedFailLog, logger._log.ToString());
        }

        public class MyLogger : IAsyncCollector<FunctionInstanceLogEntry>
        {
            public StringBuilder _log = new StringBuilder();

            string key = "k1";

            public Task AddAsync(FunctionInstanceLogEntry item, CancellationToken cancellationToken = default(CancellationToken))
            {
                // Assert flags are exclusive and exactly 1 is set. 
                int count = (item.IsStart ? 1 : 0) + (item.IsPostBind ? 1 : 0) + (item.IsCompleted ? 1 : 0);
                Assert.Equal(1, count);

                if (item.IsStart)
                {
                    Assert.Null(item.EndTime);
                    _log.Append(PreBindLog);

                    Assert.False(item.Properties.ContainsKey(key));
                    item.Properties[key] = 1;
                }
                else if (item.IsPostBind)
                {
                    var x = (int)item.Properties[key];
                    Assert.Equal(1, x); // Verify state is carried over

                    // this is still called in failure case; but hte Error message may not be set yet until the complete event. 
                    Assert.Null(item.ErrorDetails);

                    _log.Append(PostBindLog);
                }
                else if (item.IsCompleted)
                {
                    var x = (int)item.Properties[key];
                    Assert.Equal(1, x); // Verify state is carried over

                    if (item.ErrorDetails == null)
                    {
                        _log.Append(CompletedLog);
                    }
                    else
                    {
                        _log.Append(CompletedFailLog);
                    }
                }
                else
                {
                    _log.Append("Bad flags");
                    throw new InvalidOperationException("Illegal flag combination");
                }

                return Task.CompletedTask;
            }

            public Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.CompletedTask;
            }
        }

        [Binding]
        public class LoggerTestAttribute : Attribute {
            public bool Fail { get; set; }
        }

        public class TestExt : IExtensionConfigProvider
        {
            public void Initialize(ExtensionConfigContext context)
            {
                var logger = (MyLogger)context.Config.GetService<IAsyncCollector<FunctionInstanceLogEntry>>();
                context.AddBindingRule<LoggerTestAttribute>().BindToInput(attr =>
                {
                    if (attr.Fail)
                    {
                        logger._log.Append(ParamFailLog);
                        throw new InvalidOperationException("Paramter binding fored failure");
                    }
                    logger._log.Append(ParamLog);
                    return logger;
                });
            }
        }


        public class MyProg
        {
            [NoAutomaticTrigger]
            public void test([LoggerTest] MyLogger logger )
            {
                logger._log.Append(BodyLog);
            }

            [NoAutomaticTrigger]
            public void testFailParam([LoggerTest(Fail = true)] MyLogger logger)
            {
                logger._log.Append(BodyLog); // Should never get called. 
            }

            [NoAutomaticTrigger]
            public void testFailBody([LoggerTest] MyLogger logger)
            {
                logger._log.Append(BodyFailLog);
                throw new InvalidOperationException(BodyFailLog);
            }
        }
    }
}
