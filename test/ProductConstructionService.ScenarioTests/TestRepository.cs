// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.ScenarioTests;

internal class TestRepository
{
    internal const string TestOrg = "maestro-auth-test";
    internal const string TestRepo1Name = "maestro-test1";
    internal const string TestRepo2Name = "maestro-test2";
    internal const string TestRepo3Name = "maestro-test3";
    internal const string VmrTestRepoName = "maestro-test-vmr";
    internal const string SourceBranch = "master";
    internal const string TestArcadeName = "maestro-test-arcade";
    internal const string MaestroConfigurationRepoName = "maestro-configuration";

    // This branch and commit data is special for the coherency test
    // It's required to make sure that the dependency tree is set up correctly in the repo without conflicting with other test cases
    internal const string CoherencyTestRepo1Commit = "cc1a27107a1f4c4bc5e2f796c5ef346f60abb404";
    internal const string CoherencyTestRepo2Commit = "8460158878d4b7568f55d27960d4453877523ea6";
    internal const string ArcadeTestRepoCommit = "a702b2239b4dd238dd52c26a02d055cf4baff3a1";
}
