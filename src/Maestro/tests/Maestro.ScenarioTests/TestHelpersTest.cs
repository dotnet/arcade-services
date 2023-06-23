using NUnit.Framework;

namespace Maestro.ScenarioTests
{
    public class TestHelpersTests
    {
        [Test]
        public void EmptyArguments()
        {
            var formatted = TestHelpers.FormatExecutableCall("darc.exe");

            Assert.AreEqual("darc.exe", formatted);
        }

        [Test]
        public void HarmlessArguments()
        {
            var formatted = TestHelpers.FormatExecutableCall("darc.exe", new[] { "add-channel", "--name", "what-a-channel" });

            Assert.AreEqual("darc.exe \"add-channel\" \"--name\" \"what-a-channel\"", formatted);
        }

        [Test]
        public void ArgumentsWithSecretTokensInside()
        {
            var formatted = TestHelpers.FormatExecutableCall("darc.exe", new[] { "-p", "secret", "add-channel", "--github-pat", "another secret", "--name", "what-a-channel" });

            Assert.AreEqual("darc.exe \"-p\" \"***\" \"add-channel\" \"--github-pat\" \"***\" \"--name\" \"what-a-channel\"", formatted);
        }
    }
}
