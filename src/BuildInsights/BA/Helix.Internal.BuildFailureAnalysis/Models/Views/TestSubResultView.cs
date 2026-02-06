using Microsoft.TeamFoundation.Common;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models.Views
{
    public class TestSubResultView
    {
        public string TestName { get; }
        public string ErrorMessage { get; }
        public string StackTrace { get; }

        public TestSubResultView(string testName, string errorMessage, string stackTrace)
        {
            TestName = testName;
            ErrorMessage = errorMessage;
            StackTrace = stackTrace;
        }

        //The SubResult name tends to be made up of the name of the test + data driven info
        private string CreateTestSubResultName(string testName, string subResultDisplayName)
        {
            if (testName.IsNullOrEmpty())
            {
                return subResultDisplayName;
            }

            return subResultDisplayName.Replace(testName, "");
        }
    }
}
