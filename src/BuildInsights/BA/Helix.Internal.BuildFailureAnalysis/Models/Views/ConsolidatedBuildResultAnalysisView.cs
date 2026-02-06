using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using JetBrains.Annotations;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Providers;
using Microsoft.Internal.Helix.KnownIssues.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models.Views
{
    public class BasicResultsView
    {
        public bool HasData { get; set; }
        public ImmutableList<Link> PendingBuildNames { get; }
        public ImmutableList<Link> FilteredPipelinesBuildNames { get; }
        public UserSentimentParameters SentimentParameters { get; set; }
        public ImmutableList<KnownIssueView> CriticalIssues { get; set; } = ImmutableList<KnownIssueView>.Empty;

        public BasicResultsView(ImmutableList<Link> pendingBuildNames, ImmutableList<Link> filteredPipelinesBuildNames, UserSentimentParameters sentimentParameters, ImmutableList<KnownIssue> criticalIssues)
        {
            PendingBuildNames = pendingBuildNames;
            FilteredPipelinesBuildNames = filteredPipelinesBuildNames;
            SentimentParameters = sentimentParameters;
            CriticalIssues = criticalIssues.Select((c => new KnownIssueView(c.GitHubIssue.Title,
                c.GitHubIssue.LinkGitHubIssue, c.GitHubIssue.RepositoryWithOwner, c.GitHubIssue.Id.ToString(), c.GitHubIssue.LinkGitHubIssue, c.GitHubIssue.Title))).ToImmutableList();
        }

        public BasicResultsView(UserSentimentParameters sentimentParameters)
            : this(ImmutableList<Link>.Empty, ImmutableList<Link>.Empty, sentimentParameters, ImmutableList<KnownIssue>.Empty)
        {
        }
    }

    [UsedImplicitly(ImplicitUseTargetFlags.Members)]
    public class ConsolidatedBuildResultAnalysisView : BasicResultsView
    {
        /// <summary>
        /// In the form 20210226.40
        /// </summary>
        public string BuildNumber { get; set; }
        public string CheckSig { get; }
        public string SnapshotId { get; }
        public bool RepositoryHasIssues { get; }

        /// <summary>
        /// Friendly name (main, master)
        /// </summary>
        public string TargetBranch { get; set; }
        public bool IsRerun { get; set; }
        public bool HasBuildFailures => BuildFailuresUnique.Count > 0 || RepoBuildBreaks.Count > 0 || InfrastructureBuildBreaks.Count > 0;
        public bool HasTestFailures => TestFailuresUnique.Any(t => t.HasTestResults) || TestKnownIssues.Count > 0;
        public bool HasPendingBuildNames => PendingBuildNames != null && PendingBuildNames.Count > 0;
        public bool HasAutomaticRetry => BuildRetryAutomatically.Count > 0;
        public bool HasFlakyTests => FlakyTests.Count > 0;
        public bool HasTestKnownIssues => TestKnownIssues.Count > 0;
        public bool HasTestKnownIssueAnalysisUnavailablePipelines => TestKnownIssueAnalysisUnavailablePipelines.Count > 0;
        public List<TestResultsGroupView> TestFailuresUnique { get; set; } = new List<TestResultsGroupView>();
        public List<TestResultView> FlakyTests { get; set; } = new List<TestResultView>();
        public List<StepResultView> BuildFailuresUnique { get; set; } = new List<StepResultView>();
        public List<KnownIssueView> InfrastructureBuildBreaks { get; set; } = new List<KnownIssueView>();
        public List<KnownIssueView> TestKnownIssues { get; set; } = new List<KnownIssueView>();
        public List<KnownIssueView> RepoBuildBreaks { get; set; } = new List<KnownIssueView>();
        public List<RetryInformationView> BuildRetryAutomatically { get; set; } = new List<RetryInformationView>();
        public List<BuildAnalysisSummaryView> BuildAnalysisSummaries { get; set; } = new List<BuildAnalysisSummaryView>();
        public MarkdownSummarizeInstructions SummarizeInstructions { get; set; }
        public int UniqueTestFailures { get; set; }
        public int TotalBuildFailuresUnique => BuildFailuresUnique.Count;
        public ImmutableList<Link> CompletedPipelinesLinks { get; set;  }
        public ImmutableList<Link> SucceededPipelinesLinks { get; set; }
        public ImmutableList<Link> FailingPipelinesLinks { get; set; }
        public ImmutableList<Link> TestKnownIssueAnalysisUnavailablePipelines { get; set; } = ImmutableList<Link>.Empty;
        public bool RenderSummary { get; set; }

        /// <summary>
        /// Latest attempt of the build
        /// </summary>
        public List<AttemptView> LatestAttempt { get; set; } = new List<AttemptView>();

        public ConsolidatedBuildResultAnalysisView() : base(ImmutableList<Link>.Empty, ImmutableList<Link>.Empty, null, ImmutableList<KnownIssue>.Empty)
        { }

        public ConsolidatedBuildResultAnalysisView(MarkdownParameters parameters)
            : base(parameters.Analysis.PendingBuildNames, parameters.Analysis.FilteredPipelinesBuilds, BuildSentimentParameters(parameters), parameters.Analysis.CriticalIssues)
        {
            SnapshotId = parameters.SnapshotId;
            CheckSig = parameters.Analysis.CommitHash;
            RepositoryHasIssues = parameters.Repository.HasIssues;
            CompletedPipelinesLinks = parameters.Analysis.CompletedPipelines.Select(GetCompletedPipelinesLinks).ToImmutableList();
            FailingPipelinesLinks = GetFailingPipelinesLinks(parameters.Analysis.CompletedPipelines);
            SucceededPipelinesLinks = GetSucceededPipelinesLinks(parameters.Analysis.CompletedPipelines);
            TestKnownIssueAnalysisUnavailablePipelines = GetKnownIssueTestNotAnalyzedPipelinesLinks(parameters.Analysis.CompletedPipelines);

            if (CheckSig.Length > 12)
                CheckSig = CheckSig[..12];

            foreach (var pipeline in parameters.Analysis.CompletedPipelines)
            {
                List<StepResultView> buildStepResultViews = pipeline.BuildStepsResult.Select(step => new StepResultView(
                    step,
                    pipeline.PipelineName,
                    pipeline.LinkToBuild,
                    parameters
                    )).ToList();
                ImmutableList<StepResultView> pipelineUniqueBuildFailures = buildStepResultViews.Where(t => t.KnownIssues.Count == 0).ToImmutableList();
                BuildFailuresUnique.AddRange(pipelineUniqueBuildFailures);

                List<TestResultView> testFailuresUnique = pipeline.TestResults
                    .Where(t => t.TestCaseResult.Outcome == TestOutcomeValue.Failed)
                    .Where(t => t.KnownIssues.Count == 0 && !t.IsKnownIssueFailure)
                    .Select(t => new TestResultView(t, pipeline.BuildId, pipeline.LinkToBuild, parameters)).ToList();

                if (testFailuresUnique.Any())
                {
                    // For the total test failures, we don't process all the records and we only have the count
                    int uniqueTestFailuresInPipeline = pipeline.TotalTestFailures - (pipeline.TestKnownIssuesAnalysis?.TestResultWithKnownIssues?.Count ?? 0);
                    TestFailuresUnique.Add(new TestResultsGroupView(pipeline.LinkAllTestResults, pipeline.PipelineName, testFailuresUnique, uniqueTestFailuresInPipeline));
                }

                FlakyTests.AddRange(pipeline.TestResults
                    .Where(t => t.TestCaseResult.Outcome == TestOutcomeValue.PassedOnRerun)
                    .Select(t => new TestResultView(t, pipeline.BuildId, pipeline.LinkToBuild, parameters)));

                List<KnownIssueView> pipelineKnownInfrastructureBuildBreaks = buildStepResultViews
                    .Where(b => b.KnownIssues.Any(i => i.IssueType == KnownIssueType.Infrastructure))
                    .SelectMany(h => h.KnownIssues
                        .Select(f => new KnownIssueView($"{pipeline.BuildNumber} / {h.DisplayStepName}", h.LinkToBuild,
                            f.GitHubIssue.RepositoryWithOwner, f.GitHubIssue.Id.ToString(),
                            f.GitHubIssue.LinkGitHubIssue, f.GitHubIssue.Title)).ToList()).ToList();
                InfrastructureBuildBreaks.AddRange(pipelineKnownInfrastructureBuildBreaks);

                List<KnownIssueView> pipelineKnownRepositoryBuildBreaks = buildStepResultViews
                    .Where(b => b.KnownIssues.Any(i => i.IssueType == KnownIssueType.Repo))
                    .SelectMany(h => h.KnownIssues
                        .Select(f => new KnownIssueView($"{pipeline.BuildNumber} / {h.DisplayStepName}", h.LinkToBuild,
                            f.GitHubIssue.RepositoryWithOwner, f.GitHubIssue.Id.ToString(),
                            f.GitHubIssue.LinkGitHubIssue, f.GitHubIssue.Title)).ToList()).ToList();
                RepoBuildBreaks.AddRange(pipelineKnownRepositoryBuildBreaks);

                if (pipeline.TestKnownIssuesAnalysis != null)
                {
                    UniqueTestFailures += pipeline.TotalTestFailures - pipeline.TestKnownIssuesAnalysis.TestResultWithKnownIssues.Count;

                    TestKnownIssues.AddRange(pipeline.TestKnownIssuesAnalysis.TestResultWithKnownIssues.SelectMany(h =>
                        h.KnownIssues.Select(t => new KnownIssueView(h.TestCaseResult.Name, h.Url,
                            t.GitHubIssue.RepositoryWithOwner, t.GitHubIssue.Id.ToString(), t.GitHubIssue.LinkGitHubIssue, t.GitHubIssue.Title)).ToList()).ToList());
                }

                if (pipeline.LatestAttempt != null)
                {
                    LatestAttempt.Add(new AttemptView()
                    {
                        CheckSig = CheckSig,
                        LinkToBuild = pipeline.LinkToBuild,
                        AttemptId = pipeline.LatestAttempt.AttemptId,
                        BuildStepsResult = pipeline.LatestAttempt.BuildStepsResult?.Select(step => new StepResultView(step, pipeline.PipelineName, pipeline.LinkToBuild, parameters)).ToList(),
                        TestResults = pipeline.LatestAttempt.TestResults?.Select(t => new TestResultView(t, pipeline.BuildId, pipeline.LinkToBuild, parameters)).ToList()
                    });
                }

                if (pipeline.BuildAutomaticRetry != null && pipeline.BuildAutomaticRetry.HasRerunAutomatically)
                    BuildRetryAutomatically.Add(new RetryInformationView(
                        pipeline.PipelineName, pipeline.BuildId, pipeline.BuildNumber, pipeline.LinkToBuild, pipeline.BuildAutomaticRetry.GitHubIssue));


                BuildAnalysisSummaries.Add(new BuildAnalysisSummaryView(pipeline, pipelineUniqueBuildFailures.ToImmutableList(),
                    pipelineKnownInfrastructureBuildBreaks.ToImmutableList(), pipelineKnownRepositoryBuildBreaks.ToImmutableList(), parameters));
            }
            SentimentParameters.SnapshotId = SnapshotId;
            // It feels like these are weirdly complicated, and perhaps this logic should be done here,
            // rather than in the templates, and the templates essentially have no logic in them
            // As of now, it's highly possible they don't actually match the rendering behavior
            SentimentParameters.HasUniqueBuildFailures = BuildFailuresUnique.Count > 0;
            SentimentParameters.HasUniqueTestFailures = TestFailuresUnique.Any(t => t.HasTestResults);
            SentimentParameters.IsRetryWithUniqueBuildFailures = !SentimentParameters.HasUniqueBuildFailures.Value &&
                LatestAttempt.Any(a => a.HasBuildFailures);
            SentimentParameters.IsRetryWithUniqueTestFailures = !SentimentParameters.HasUniqueTestFailures.Value &&
                LatestAttempt.Any(a => a.HasTestFailures);
            SentimentParameters.KnownIssues = InfrastructureBuildBreaks.Count + RepoBuildBreaks.Count;
            TargetBranch = parameters.Analysis.CompletedPipelines?.FirstOrDefault()?.TargetBranch?.BranchName ?? "unknown";
            IsRerun = LatestAttempt.Count > 0;
            HasData = parameters.Analysis.CompletedPipelines.Count != 0;
            RenderSummary = parameters.SummarizeInstructions?.GenerateSummaryVersion ?? false;
            SummarizeInstructions = parameters.SummarizeInstructions;
            InfrastructureBuildBreaks = InfrastructureBuildBreaks.GroupBy(k => k, new KnownIssueViewComparer()).Select( g => new  KnownIssueView(g.Key, g.Count())).ToList();
            RepoBuildBreaks = RepoBuildBreaks.GroupBy(k => k, new KnownIssueViewComparer()).Select(g => new KnownIssueView(g.Key, g.Count())).ToList(); ;
            TestKnownIssues = TestKnownIssues.GroupBy(k => k, new KnownIssueViewComparer()).Select(g => new KnownIssueView(g.Key, g.Count())).ToList(); ;
            BuildAnalysisSummaries = BuildAnalysisSummaries.OrderBy(t => t.BuildStatus).ToList();
            TestFailuresUnique = TestGroupViewHelper.DistributeDisplayedTestResults(TestFailuresUnique);
        }

        private static UserSentimentParameters BuildSentimentParameters(MarkdownParameters parameters)
        {
            return new UserSentimentParameters
            {
                Repository = parameters.Repository.Id,
                CommitHash = parameters.Analysis.CommitHash,
            };
        }

        private static Link GetCompletedPipelinesLinks(BuildResultAnalysis analysis)
        {
            return new Link(analysis.PipelineName, analysis.LinkToBuild);
        }

        private static ImmutableList<Link> GetFailingPipelinesLinks(ImmutableList<BuildResultAnalysis> analysis)
        {
            return analysis.Where(b => b.BuildStatus == BuildStatus.Failed)
                .Select(t => new Link($"[{t.PipelineName}]", t.LinkToBuild)).ToImmutableList();
        }

        private static ImmutableList<Link> GetSucceededPipelinesLinks(ImmutableList<BuildResultAnalysis> analysis)
        {
            return analysis.Where(b => b.BuildStatus == BuildStatus.Succeeded)
                .Select(t => new Link($"[{t.PipelineName}]", t.LinkToBuild)).ToImmutableList();
        }

        private static ImmutableList<Link> GetKnownIssueTestNotAnalyzedPipelinesLinks(ImmutableList<BuildResultAnalysis> analysis)
        {
            return analysis.Where(t => !t.TestKnownIssuesAnalysis?.IsAnalysisAvailable ?? false)
                .Select(t => new Link($"[{t.PipelineName}]", t.LinkToBuild)).ToImmutableList();
        }
    }
}
