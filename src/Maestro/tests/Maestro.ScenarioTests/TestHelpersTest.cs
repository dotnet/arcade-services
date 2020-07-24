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

        [Test]
        public void ParseOutSpecificRepoPolicy_ExactMatch()
        {
            string exactMatchPolicy =
                @"https://github.com/maestro-auth-test/maestro-test1 @ RepoPoliciesTestBranch_DESKTOP-8VPP7ON
                    -Merge Policies:
                    AllChecksSuccessful
                      ignoreChecks =
          
                                     [
                                       ""A"",
                                       ""B""
                                     ]";

            string parsedPolicy = TestHelpers.ParseOutSpecificRepoPolicy(exactMatchPolicy,
               "https://github.com/maestro-auth-test/maestro-test1 @ RepoPoliciesTestBranch_DESKTOP-8VPP7ON");

            string expectedPolicy =
                $"RepoPoliciesTestBranch_DESKTOP-8VPP7ON\r\n- Merge Policies:\r\n  AllChecksSuccessful\r\n    ignoreChecks = \r\n" +
                "                   [\r\n" +
                "                     \"A\",\r\n" +
                "                     \"B\"\r\n" +
                "                   ]\r\n";

            StringAssert.AreNotEqualIgnoringCase(expectedPolicy, parsedPolicy);
        }

        [Test]
        public void ParseOutSpecificRepoPolicy_MultipleResults_Last()
        {
            string extraPolicies = @"https://github.com/maestro-auth-test/maestro-test1 @ RepoPoliciesTestBranch
                - Merge Policies:[]
            https://github.com/maestro-auth-test/maestro-test1 @ RepoPoliciesTestBranch_2
                -Merge Policies:
                AllChecksSuccessful
                  ignoreChecks =
          
                                 [
                                   ""A"",
                                   ""B""
                                 ]";

            string parsedPolicy = TestHelpers.ParseOutSpecificRepoPolicy(extraPolicies, 
                "https://github.com/maestro-auth-test/maestro-test1 @ RepoPoliciesTestBranch_2");

            string expectedPolicy =
                            $"RepoPoliciesTestBranch_2\r\n- Merge Policies:\r\n  AllChecksSuccessful\r\n    ignoreChecks = \r\n" +
                "                   [\r\n" +
                "                     \"A\",\r\n" +
                "                     \"B\"\r\n" +
                "                   ]\r\n";

            StringAssert.AreNotEqualIgnoringCase(expectedPolicy, parsedPolicy);
        }

        [Test]
        public void ParseOutSpecificRepoPolicy_MultipleResults_First()
        {
            string extraPolicies = @"https://github.com/maestro-auth-test/maestro-test1 @ RepoPoliciesTestBranch
                - Merge Policies:[]
            https://github.com/maestro-auth-test/maestro-test1 @ RepoPoliciesTestBranch_2
                -Merge Policies:
                AllChecksSuccessful
                  ignoreChecks =
          
                                 [
                                   ""A"",
                                   ""B""
                                 ]";

            string parsedPolicy = TestHelpers.ParseOutSpecificRepoPolicy(extraPolicies,
                "https://github.com/maestro-auth-test/maestro-test1 @ RepoPoliciesTestBranch");

            string expectedPolicy =
                            $"RepoPoliciesTestBranch\r\n- Merge Policies:\r\n  AllChecksSuccessful\r\n    ignoreChecks = \r\n" +
                "                   [\r\n" +
                "                     \"A\",\r\n" +
                "                     \"B\"\r\n" +
                "                   ]\r\n";

            StringAssert.AreNotEqualIgnoringCase(expectedPolicy, parsedPolicy);
        }

        [Test]
        public void ParseOutSpecificRepoPolicy_MultipleResults_Middle()
        {
            string extraPolicies = @"https://github.com/maestro-auth-test/maestro-test1 @ RepoPoliciesTestBranch
                - Merge Policies:[]
            https://github.com/maestro-auth-test/maestro-test1 @ RepoPoliciesTestBranch_2
                - Merge Policies:[]
            https://github.com/maestro-auth-test/maestro-test1 @ RepoPoliciesTestBranch_3
                -Merge Policies:
                AllChecksSuccessful
                  ignoreChecks =
          
                                 [
                                   ""A"",
                                   ""B""
                                 ]";

            string parsedPolicy = TestHelpers.ParseOutSpecificRepoPolicy(extraPolicies,
                "https://github.com/maestro-auth-test/maestro-test1 @ RepoPoliciesTestBranch_2");

            string expectedPolicy =
                            $"RepoPoliciesTestBranch_2\r\n- Merge Policies:\r\n  AllChecksSuccessful\r\n    ignoreChecks = \r\n" +
                "                   [\r\n" +
                "                     \"A\",\r\n" +
                "                     \"B\"\r\n" +
                "                   ]\r\n";

            StringAssert.AreNotEqualIgnoringCase(expectedPolicy, parsedPolicy);
        }
    }
}
