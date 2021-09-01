using NUnit.Framework;
using System.IO;

namespace TestProject1
{
    [TestFixture]
    public class UnitTest1
    {
        [Test]
        public void TestMethod1()
        {
            if(!File.Exists("test.txt"))
            {
                File.Create("test.txt");
                Assert.Fail("test.txt does not exist, creating");
            }
            else
            {
                File.Delete("test.txt");
                Assert.IsTrue(true, "test.txt found, deleting");                
            }

        }
    }
}
