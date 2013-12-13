using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AzureTables;
using DaasEndpoints;
using Executor;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Orchestrator;
using RunnerInterfaces;
using SimpleBatch;

namespace OrchestratorUnitTests
{
    [TestClass]
    public class PrereqManagerTests
    {
        static Guid F1 = Guid.Parse("{10000000-0000-0000-0000-000000000000}");
        static Guid F2 = Guid.Parse("{20000000-0000-0000-0000-000000000000}");
        static Guid G1 = Guid.Parse("{30000000-0000-0000-0000-000000000000}");
        static Guid G2 = Guid.Parse("{40000000-0000-0000-0000-000000000000}");

        [TestMethod]
        public void NoPrereqs()
        {
            var w = new World();

            w.AddPrereq(G1, new Guid[] { });
            Assert.IsTrue(w.ActivatedSet.Contains(G1));
        }

        [TestMethod]
        public void Simple()
        {
            var w = new World();
            
            w.AddPrereq(G1, new Guid[] { F1, F2 });
            Assert.AreEqual(0, w.ActivatedSet.Count, "should not have activated yet");
            Assert.AreEqual(2, w.EnumeratePrereqs(G1).Count);

            w.OnComplete(F1);
            Assert.AreEqual(0, w.ActivatedSet.Count, "should not have activated yet");

            var outstanding = w.EnumeratePrereqs(G1);
            Assert.AreEqual(1, outstanding.Count);
            Assert.IsTrue(outstanding.Contains(F2));


            w.OnComplete(F2);
            Assert.AreEqual(1, w.ActivatedSet.Count);
            Assert.IsTrue(w.ActivatedSet.Contains(G1));
            Assert.AreEqual(0, w.EnumeratePrereqs(G1).Count);
        }

        [TestMethod]
        public void SimpleDoubleComplete()
        {
            var w = new World();

            w.AddPrereq(G1, new Guid[] { F1, F2 });
            Assert.AreEqual(0, w.ActivatedSet.Count, "should not have activated yet");

            w.OnComplete(F1);
            Assert.AreEqual(0, w.ActivatedSet.Count, "should not have activated yet");

            w.OnComplete(F1);
            Assert.AreEqual(0, w.ActivatedSet.Count, "double complete on same call shouldn't activate");

            w.OnComplete(F2);
            Assert.AreEqual(1, w.ActivatedSet.Count);
            Assert.IsTrue(w.ActivatedSet.Contains(G1));
        }

        [TestMethod]
        public void MultipleSuccessors()
        {
            var w = new World();

            w.AddPrereq(G1, new Guid[] { F1 });
            w.AddPrereq(G2, new Guid[] { F1 });
            Assert.AreEqual(0, w.ActivatedSet.Count, "should not have activated yet");
            
            w.OnComplete(F1);
            Assert.AreEqual(2, w.ActivatedSet.Count, "should not have activated yet");
            Assert.IsTrue(w.ActivatedSet.Contains(G1));
            Assert.IsTrue(w.ActivatedSet.Contains(G2));
        }

        [TestMethod]
        public void AllPrereqAlreadyDone()
        {
            var w = new World();

            w.Status[F1] = FunctionInstanceStatus.CompletedSuccess;
            
            w.AddPrereq(G1, new Guid[] { F1 });
            Assert.IsTrue(w.ActivatedSet.Contains(G1), "Should have run immediately since prereqs were complete");
        }
        
        // 1 prereq is already completed, 1 is outstanding
        [TestMethod]
        public void SimpleSplit()
        {
            var w = new World();

            w.Status[F1] = FunctionInstanceStatus.CompletedSuccess;

            w.AddPrereq(G1, new Guid[] { F1, F2 });
            Assert.AreEqual(0, w.ActivatedSet.Count, "should not have activated yet");
                        
            w.OnComplete(F2);
            Assert.AreEqual(1, w.ActivatedSet.Count);
            Assert.IsTrue(w.ActivatedSet.Contains(G1));
        }

        [TestMethod]
        public void NoSuccessors()
        {
            var w = new World();

            w.OnComplete(F2); // No successors. 
            Assert.AreEqual(0, w.ActivatedSet.Count);
        }

        [TestMethod]
        public void FailedPrereq()
        {
            var w = new World();

            w.Status[F1] = FunctionInstanceStatus.CompletedFailed;

            w.AddPrereq(G1, new Guid[] { F1 });

            // G1 should not run. Ambiguous whether it fails outright or is forwever "waiting prereqs".
            Assert.AreEqual(0, w.ActivatedSet.Count, "should not have activated yet");
            
            w.OnComplete(F1); 
            Assert.AreEqual(0, w.ActivatedSet.Count);
        }

        class World
        {
            private MockIActivateFunction Activator = new MockIActivateFunction();
            public IPrereqManager PrereqManager;

            // Sets status of prerequisite functions. 
            public IDictionary<Guid, FunctionInstanceStatus> Status = new Dictionary<Guid, FunctionInstanceStatus>();

            // Get the set of functions that we've called IActivator for. 
            public HashSet<Guid> ActivatedSet 
            { 
                get 
                {                     
                    return this.Activator.Activated; 
                } 
            }

            public World()
            {
                IAzureTable prereqTable = AzureTable.NewInMemory();
                IAzureTable successorTable = AzureTable.NewInMemory();

                Func<Guid, FunctionInstanceStatus> fpIsDone = guid =>
                    {
                        FunctionInstanceStatus status;
                        if (this.Status.TryGetValue(guid, out status))
                        {
                            return status;
                        }
                        return FunctionInstanceStatus.None;
                    };

                this.PrereqManager = new PrereqManager(prereqTable, successorTable, fpIsDone);
            }

            public void AddPrereq(Guid func, IEnumerable<Guid> prereqs)
            {
                this.PrereqManager.AddPrereq(func, prereqs, this.Activator);
            }

            public HashSet<Guid> EnumeratePrereqs(Guid func)
            {
                return new HashSet<Guid>(this.PrereqManager.EnumeratePrereqs(func));
            }

            public void OnComplete(Guid g)
            {
                this.PrereqManager.OnComplete(g, Activator);
            }

            class MockIActivateFunction : IActivateFunction
            {
                public HashSet<Guid> Activated = new HashSet<Guid>();
                void IActivateFunction.ActivateFunction(Guid instance)
                {
                    Activated.Add(instance);
                }
            }
        }
    }
}