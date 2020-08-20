using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using System.Text;

namespace Maestro.ScenarioTests
{
    internal class TestRepository
    {
        internal static string TestRepo1Name => "maestro-test1";
        internal static string TestRepo2Name => "maestro-test2";
        internal static string TestRepo3Name => "maestro-test3";
        internal static string SourceBranch => "master";

        // This branch and commit data is special for the coherency test
        // It's required to make sure that the dependency tree is set up correctly in the repo without conflicting with other test cases
        internal static string CoherencySourceBranch => "coherency-tree";
        internal static string CoherencyTestRepo1Commit => "cc1a27107a1f4c4bc5e2f796c5ef346f60abb404";
        internal static string CoherencyTestRepo2Commit => "8460158878d4b7568f55d27960d4453877523ea6";
    }
}
