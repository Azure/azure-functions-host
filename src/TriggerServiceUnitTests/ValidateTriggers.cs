using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs;


namespace Microsoft.WindowsAzure.JobsUnitTests
{
    [TestClass]
    public class ValidateTests
    {
        [TestMethod]
        public void BadContainerName()
        {
            // Output has unbound token not found in input
            var trigger = TriggerRaw.NewBlob("callback", "Uppercase"); // must be lowercase

            try
            {
                Validator.ValidateBlobTrigger(trigger);
                Assert.Fail("container name can't have uppercase");
            }
            catch { }
        }

        [TestMethod]
        public void BadContainerCantHaveRouteParameters()
        {
            // Output has unbound token not found in input
            var trigger = TriggerRaw.NewBlob("callback", "Container{route}/Blob"); // must be lowercase

            try
            {
                // Note that {} syntax is illegal container name anyways.
                Validator.ValidateBlobTrigger(trigger);
                Assert.Fail("container name can't have route parameters");
            }
            catch { }
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ValidateUnboundRouteParametersInOutput()
        {
            // Output has unbound token not found in input
            var trigger = TriggerRaw.NewBlob("callback", "container/Blob", "container/{name}.txt");
            Validator.ValidateBlobTrigger(trigger);
        }

        [TestMethod]
        public void ParseErrorInInput()
        {
            // Output has unbound token not found in input
            // Ok to construct. 
            var trigger = TriggerRaw.NewBlob("callback", "container/Blob{");

            try
            {
                Validator.ValidateBlobTrigger(trigger);
                Assert.Fail("validation should have failed on parse error");
            }
            catch
            {
            }
        }
    }
}