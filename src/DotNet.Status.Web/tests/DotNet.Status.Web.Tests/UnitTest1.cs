using System;
using Xunit;
using DotNet.Status.Web.Controllers;

namespace DotNet.Status.Web.Tests
{
    public class UnitTest1
    {
        [Fact]
        public void Test1()
        {
            // Create test to call collectArcadeValidation
            var controller = new TelemetryController(); // DI these parameters
            var result = controller.CollectArcadeValidation(new ArcadeValidationData
            {

            });
            Assert.NotNull(result);
        }
    }
}
