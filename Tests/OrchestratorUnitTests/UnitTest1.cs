using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Orchestrator;
using RunnerHost;
using RunnerInterfaces;

namespace OrchestratorUnitTests
{
    [TestClass]
    public class OrchestratorWorker
    {
        // Sequential times.
        static DateTime Old = new DateTime(1900, 1, 1);
        static DateTime Middle = new DateTime(1900, 1, 2);
        static DateTime Newer = new DateTime(1900, 1, 3);

        [TestMethod]
        public void NoOutputs()
        {
            // Only have input. This is scary case because it's like a short circuit.
            // Have to be able to run input at least once. But nothing to prevent us from re-running.
            var x = Worker.CheckBlobTimes(
                Runtime(Blob(Old)),
                Static(
                    new BlobParameterStaticBinding { IsInput = true }
                )
                );

            // No outputs, so call the issue moot.
            // This means function can get rerun arbitrary number of times. 
            // (possibly as frequently as the container is scanned)
            Assert.AreEqual(null, x);
        }

        [TestMethod]
        public void NoInputs()
        {
            // Only have input. This is scary case because it's like a short circuit.
            // Have to be able to run input at least once. But nothing to prevent us from re-running.
            var x = Worker.CheckBlobTimes(
                Runtime(Blob(Old)),
                Static(
                    new BlobParameterStaticBinding { IsInput = false }
                )
                );

            // No outputs, so call the issue moot.
            // This means function can get rerun arbitrary number of times. 
            // (possibly as frequently as the container is scanned)
            Assert.AreEqual(null, x);
        }

        [TestMethod]
        public void NewerInput()
        {
            // Input is newer than output. Execute
            var x = Worker.CheckBlobTimes(
                Runtime(Blob(Newer), Blob(Old)),
                Static(
                    new BlobParameterStaticBinding { IsInput = true },
                    new BlobParameterStaticBinding { IsInput = false }
                )
                );

            Assert.AreEqual(true, x);
        }

        [TestMethod]
        public void NoBlobOutput()
        {
            // There is no output. Should execute.
            var x = Worker.CheckBlobTimes(
                Runtime(Blob(Newer), Blob(null)),
                Static(
                    new BlobParameterStaticBinding { IsInput = true },
                    new BlobParameterStaticBinding { IsInput = false }
                )
                );

            Assert.AreEqual(true, x);
        }

        [TestMethod]
        public void OlderInput()
        {
            // Input is older than output. Don't need to execute.
            var x = Worker.CheckBlobTimes(
                Runtime(Blob(Old), Blob(Newer)),
                Static(
                    new BlobParameterStaticBinding { IsInput = true },
                    new BlobParameterStaticBinding { IsInput = false }
                )
                );

            Assert.AreEqual(false, x);
        }

        [TestMethod]
        public void StrippedInput()
        {
            // Input is newer than output. Execute
            var x = Worker.CheckBlobTimes(
                Runtime(
                    Blob(Old),
                    Blob(Middle), 
                    Blob(Newer)),
                Static(
                    new BlobParameterStaticBinding { IsInput = true },
                    new BlobParameterStaticBinding { IsInput = false },
                    new BlobParameterStaticBinding { IsInput = true }
                )
                );

            Assert.AreEqual(true, x);
        }

        [TestMethod]
        public void StrippedInput2()
        {
            // Input is newer than output. Execute
            var x = Worker.CheckBlobTimes(
                Runtime(
                    Blob(Old),
                    Blob(Middle),
                    Blob(Newer)),
                Static(
                    new BlobParameterStaticBinding { IsInput = false },
                    new BlobParameterStaticBinding { IsInput = true },
                    new BlobParameterStaticBinding { IsInput = false }
                )
                );

            Assert.AreEqual(true, x);
        }

        [TestMethod]
        public void WithTableNewInput()
        {
            // Input newer than output. And we have a read-only table. 
            var x = Worker.CheckBlobTimes(
                Runtime(
                    Blob(Newer),
                    Blob(Old),
                    new TableParameterRuntimeBinding()),
                Static(
                    new BlobParameterStaticBinding { IsInput = true },
                    new BlobParameterStaticBinding { IsInput = false },
                    new TableParameterStaticBinding { IsReadOnly = true }
                )
                );

            Assert.AreEqual(true, x);
        }



        [TestMethod]
        public void WithTableOldInput()
        {
            // Input older than output. And we have a read-only table. 
            var x = Worker.CheckBlobTimes(
                Runtime(
                    Blob(Old),
                    Blob(Newer),
                    new TableParameterRuntimeBinding()),
                Static(
                    new BlobParameterStaticBinding { IsInput = true },
                    new BlobParameterStaticBinding { IsInput = false },
                    new TableParameterStaticBinding { IsReadOnly = true }
                )
                );

            Assert.AreEqual(false, x);
        }


        [TestMethod]
        public void NewerInputAndParam()
        {
            // Input is newer than output, has extra route arg parameter (which shouldn't impact things).
            // Should still Execute
            var x = Worker.CheckBlobTimes(
                Runtime(
                    Blob(Newer), 
                    new LiteralStringParameterRuntimeBinding(),
                    Blob(Old)),
                Static(
                    new BlobParameterStaticBinding { IsInput = true },
                    new NameParameterStaticBinding(),
                    new BlobParameterStaticBinding { IsInput = false }
                )
                );

            Assert.AreEqual(true, x);
        }

        [TestMethod]
        public void OlderInputAndParam()
        {
            // Output is newer than input, has extra route arg parameter (which shouldn't impact things).
            // Don't execute.
            var x = Worker.CheckBlobTimes(
                Runtime(
                    Blob(Old),
                    new LiteralStringParameterRuntimeBinding(),
                    Blob(Newer)),
                Static(
                    new BlobParameterStaticBinding { IsInput = true },
                    new NameParameterStaticBinding(),
                    new BlobParameterStaticBinding { IsInput = false }
                )
                );

            Assert.AreEqual(false, x);
        }   

        static ParameterRuntimeBinding[] Runtime(params ParameterRuntimeBinding[] argsRuntime)
        {
            return argsRuntime;
        }

        static ParameterStaticBinding[] Static(params ParameterStaticBinding[] flows)
        {
            return flows;
        }

        static ParameterRuntimeBinding Blob(DateTime? time)
        {
            return new MockBlobParameterRuntimeBinding { _lastModifiedTime = time };
        }

        class MockBlobParameterRuntimeBinding : BlobParameterRuntimeBinding
        {
            public DateTime? _lastModifiedTime;

            public override DateTime? LastModifiedTime
            {
	            get 
	            { 
		             return _lastModifiedTime;
	            }
            }
        }
    }


    
}
