using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Operations.Clone;
using Moq;
using Xunit;

namespace Microsoft.DotNet.Darc.Tests.Operations
{
    public class CloneOperationTests
    {

        [Fact]
        public void TrySomethingTest()
        {
            CloneOperation op = new CloneOperation(null);
            var opm = new Mock<CloneOperation>(MockBehavior.Strict);
            opm.Setup(o => o.A()).
        }
    }
}
