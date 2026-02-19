using AwesomeAssertions;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using NUnit.Framework;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Tests.Models;

[TestFixture]
public class PipelineReferenceTests
{
    [Test]
    public void PipelineReferenceAreEqual()
    {
        PipelineReference piePipelineReferenceA =
            MockPipelineReference("StageNameTest", "PhaseNameTest", "JobNameTest");
        PipelineReference piePipelineReferenceB =
            MockPipelineReference("StageNameTest", "PhaseNameTest", "JobNameTest");

        piePipelineReferenceA.Equals(piePipelineReferenceB).Should().BeTrue();
    }

    [Test]
    public void PipelineReferenceAreDifferent()
    {
        PipelineReference piePipelineReferenceA =
            MockPipelineReference("StageNameTest", "PhaseNameTest", "JobNameTest");
        PipelineReference piePipelineReferenceB =
            MockPipelineReference("StageNameTest", "PhaseNameTest", "JobNameNotTest");

        piePipelineReferenceA.Equals(piePipelineReferenceB).Should().BeFalse();
    }

    [Test]
    public void PipelineReferenceAreEqualWhenNameHaveHierarchy()
    {
        PipelineReference piePipelineReferenceA = MockPipelineReference("Stage", "Phase", "Job");
        PipelineReference piePipelineReferenceB = MockPipelineReference("Stage", "Stage.Phase", "Stage.Phase.Job");

        piePipelineReferenceB.Equals(piePipelineReferenceA).Should().BeTrue();
        piePipelineReferenceA.Equals(piePipelineReferenceB).Should().BeTrue();
        piePipelineReferenceA.Equals(piePipelineReferenceA).Should().BeTrue();
        piePipelineReferenceB.Equals(piePipelineReferenceB).Should().BeTrue();
    }

    [Test]
    public void PipelineReferenceAreEqualWhenNameHaveHierarchyByStage()
    {
        PipelineReference piePipelineReferenceA = MockPipelineReference("Stage", "Phase", "Job");
        PipelineReference piePipelineReferenceB = MockPipelineReference("Stage", "Phase", "Phase.Job");

        piePipelineReferenceB.Equals(piePipelineReferenceA).Should().BeTrue();
        piePipelineReferenceA.Equals(piePipelineReferenceB).Should().BeTrue();
        piePipelineReferenceA.Equals(piePipelineReferenceA).Should().BeTrue();
        piePipelineReferenceB.Equals(piePipelineReferenceB).Should().BeTrue();
    }

    public PipelineReference MockPipelineReference(string stageName, string phaseName, string jobName)
    {
        return new PipelineReference(
            new StageReference(stageName, 1),
            new PhaseReference(phaseName, 1),
            new JobReference(jobName, 1)
        );
    }
}
