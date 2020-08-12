using AutoFixture.NUnit3;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.IO;
using System.Reflection;

namespace DotNet.Status.Web.Tests
{
    [TestFixture]
    public class TimeLineIssueServiceLogicTests
    {
        private TimelineIssueTriageService.TimelineIssueTriageLogic _logic => new TimelineIssueTriageService.TimelineIssueTriageLogic();

        [TestCase("")]
        [TestCase("[]")]
        [TestCase("[Movies=aha]")]
        [TestCase("[Movies](http://that.movie.yo)")]
        [TestCase("Build\nIndex\nRecord\n")]
        public void ParseZeroBuildInfoFromNotTriageIssue(string body)
        {
            var result = _logic.GetTriageItemsProperties(body);
            result.Should().BeEmpty();
        }

        [TestCase("[]")]
        [TestCase("[Movies=aha]")]
        [TestCase("[Movies](http://that.movie.yo)")]
        [TestCase("Build RecordId Index")]
        [TestCase("a\n[BuildId=534863,RecordId=041c699a-ea24-59c7-817f-32bc9df73765,Index=0]\ntext[Category=Build]")]
        [TestCase("a\n[BuildId=534863,RecordId=041c699a-ea24-59c7-817f-32bc9df73765,Index=0][Category=Build]")]
        [TestCase("a\n[BuildId=534863,RecordId=041c699a-ea24-59c7-817f-32bc9df73765,Index=0]")]
        [TestCase("a\n[BuildId=534863,RecordId=041c699a-ea24-59c7-817f-32bc9df73765,Index=0]\ntext[Category=Build]")]
        [TestCase("a\n[BuildId=,RecordId=041c699a-ea24-59c7-817f-32bc9df73765,Index=0]\ntext[Category=Build]")]
        [TestCase("a\n[BuildId=534863,RecordId=,Index=0]\ntext[Category=Build]")]
        [TestCase("a\n[BuildId=534863,RecordId=041c699a-ea24-59c7-817f-32bc9df73765,Index=]\ntext[Category=Build]")]
        [TestCase("a\n[BuildId=534863,RecordId=041c699a-ea24-59c7-817f-32bc9df73765,Index=0]\ntext[Category=]")]
        [TestCase("a\n[BuildId=534863,RecordId=041c699a-ea24-59c7-817f-32bc9df73765,Index=0]\n[Category = Build]")]
        [TestCase("a\n[BuildId=534863,RecordId=041c699a-ea24-59c7-817f-32bc9df73765,Index=0]\n[Category =Build]")]
        public void ParseZeroBuildInfoFromNotFullBuildInfosInTriageIssue(string body)
        {
            var result = _logic.GetTriageItemsProperties(body);
            result.Should().BeEmpty();
        }

        [TestCase("[BuildId=534863,RecordId=041c699a-ea24-59c7-817f-32bc9df73765,Index=0]\n[Category=Build]", 
            534863, "041c699a-ea24-59c7-817f-32bc9df73765", 0, "Build")]
        [TestCase("\n[BuildId=534863,RecordId=041c699a-ea24-59c7-817f-32bc9df73765,Index=12]\n[Category=Build]",
            534863, "041c699a-ea24-59c7-817f-32bc9df73765", 12, "Build")]
        [TestCase("\nsome text[BuildId=534863,RecordId=041c699a-ea24-59c7-817f-32bc9df73765,Index=13]\n[Category=Build]",
            534863, "041c699a-ea24-59c7-817f-32bc9df73765", 13, "Build")]
        [TestCase("[BuildId=534863,RecordId=not-correct-syntax,Index=13]\n[Category=Build]",
            534863, "00000000-0000-0000-0000-000000000000", 13, "Build")]
        [TestCase("[BuildId=not-correct,RecordId=041c699a-ea24-59c7-817f-32bc9df73765,Index=13]\n[Category= Build ]",
            default(int), "041c699a-ea24-59c7-817f-32bc9df73765", 13, " Build ")]
        [TestCase("[BuildId=534863,RecordId=041c699a-ea24-59c7-817f-32bc9df73765,Index=not-correct]\n[Category=Bui ld]",
            534863, "041c699a-ea24-59c7-817f-32bc9df73765", default(int), "Bui ld")]
        [TestCase("[BuildId=534863,RecordId=041c699a-ea24-59c7-817f-32bc9df73765,Index=13]\n[Category=123]",
            534863, "041c699a-ea24-59c7-817f-32bc9df73765", 13, "")]
        public void ParseSingleBuildInfoFromTriageIssue(string body, int buildId, string recordId, int index, string category)
        {
            var result = _logic.GetTriageItemsProperties(body);
            result.Should().HaveCount(1);
            var expected = new TriageItem { BuildId = buildId, RecordId = Guid.Parse(recordId), Index = index, UpdatedCategory = category };
            result.Should().Contain(expected);
        }

        [TestCase("triage-items-with-multi-builds")]
        [TestCase("triage-items-with-diff-cat")]
        [TestCase("triage-items-with-invalid")]
        public void ParseMultipleBuildInfoFromTriageIssue(string fileName)
        {
            var body = GetTestPayload($"{fileName}.body.txt");
            var expected = JsonConvert.DeserializeObject<TriageItem[]>(GetTestPayload($"{fileName}.expected.json"));
            var result = _logic.GetTriageItemsProperties(body);
            result.Should().Equal(expected, IsParsedTriageEqual);
        }

        private bool IsParsedTriageEqual(TriageItem left, TriageItem right)
        {
            return
                left.BuildId         == right.BuildId &&
                left.RecordId        == right.RecordId &&
                left.Index           == right.Index &&
                left.UpdatedCategory == right.UpdatedCategory;
        }

        [Test]
        public void DetectDuplicateIfIssuesAreSame()
        {
            var a = new[]
            {
                new TriageItem { BuildId = 10, RecordId = Guid.Parse("041c699a-ea24-59c7-817f-32bc9df73730"), Index = 1 },
                new TriageItem { BuildId = 20, RecordId = Guid.Parse("041c699a-ea24-59c7-817f-32bc9df73720"), Index = 2 },
                new TriageItem { BuildId = 30, RecordId = Guid.Parse("041c699a-ea24-59c7-817f-32bc9df73710"), Index = 3 },
            };
            var b = new[]
            {
                new TriageItem { BuildId = 20, RecordId = Guid.Parse("041c699a-ea24-59c7-817f-32bc9df73720"), Index = 2 },
                new TriageItem { BuildId = 30, RecordId = Guid.Parse("041c699a-ea24-59c7-817f-32bc9df73710"), Index = 3 },
                new TriageItem { BuildId = 10, RecordId = Guid.Parse("041c699a-ea24-59c7-817f-32bc9df73730"), Index = 1 },
            };

            _logic.IsDuplicate(a, b).Should().BeTrue();
            _logic.IsDuplicate(b, a).Should().BeTrue();
        }

        [Test]
        public void NotDetectDuplicateIfOnlyOverlap()
        {
            var a = new[]
            {
                new TriageItem { BuildId = 10, RecordId = Guid.Parse("041c699a-ea24-59c7-817f-32bc9df73730"), Index = 1 },
                new TriageItem { BuildId = 20, RecordId = Guid.Parse("041c699a-ea24-59c7-817f-32bc9df73720"), Index = 2 },
                new TriageItem { BuildId = 30, RecordId = Guid.Parse("041c699a-ea24-59c7-817f-32bc9df73710"), Index = 3 },
            };
            var b = new[]
            {
                new TriageItem { BuildId = 20, RecordId = Guid.Parse("041c699a-ea24-59c7-817f-32bc9df73720"), Index = 2 },
                new TriageItem { BuildId = 30, RecordId = Guid.Parse("041c699a-ea24-59c7-817f-32bc9df73710"), Index = 3 },
                new TriageItem { BuildId = 40, RecordId = Guid.Parse("041c699a-ea24-59c7-817f-32bc9df73740"), Index = 4 },
                new TriageItem { BuildId = 10, RecordId = Guid.Parse("041c699a-ea24-59c7-817f-32bc9df73730"), Index = 1 },
            };

            _logic.IsDuplicate(a, b).Should().BeFalse();
            _logic.IsDuplicate(b, a).Should().BeFalse();
        }

        [Test]
        public void NotDetectDuplicateIfNotIntersect()
        {
            var a = new[]
            {
                new TriageItem { BuildId = 10, RecordId = Guid.Parse("041c699a-ea24-59c7-817f-32bc9df73730"), Index = 1 },
                new TriageItem { BuildId = 20, RecordId = Guid.Parse("041c699a-ea24-59c7-817f-32bc9df73720"), Index = 2 },
                new TriageItem { BuildId = 30, RecordId = Guid.Parse("041c699a-ea24-59c7-817f-32bc9df73710"), Index = 3 },
            };
            var b = new[]
            {
                new TriageItem { BuildId = 40, RecordId = Guid.Parse("041c699a-ea24-59c7-817f-32bc9df73740"), Index = 4 },
                new TriageItem { BuildId = 50, RecordId = Guid.Parse("041c699a-ea24-59c7-817f-32bc9df73750"), Index = 5 },
                new TriageItem { BuildId = 60, RecordId = Guid.Parse("041c699a-ea24-59c7-817f-32bc9df73760"), Index = 6 },
            };

            _logic.IsDuplicate(a, b).Should().BeFalse();
            _logic.IsDuplicate(b, a).Should().BeFalse();
        }

        [TestCase("[Category=Build]", "Category", "Build")]
        [TestCase("[ Category =Build]", " Category ", "Build")]
        [TestCase("[\nCate\ngory\n=Build]", "\nCate\ngory\n", "Build")]
        [TestCase("[Category=First][Category=Next]", "Category", "First")]
        [TestCase("[Category=][Category=Next]", "Category", "Next")]
        [TestCase("[Category=Build][Something=Nice]", "Something", "Nice")]
        [TestCase("[Category=Build][Something=Nice]", "Category", "Build")]
        [TestCase("[Category=Build]\n[Something=Nice]", "Something", "Nice")]
        [TestCase("[Category=Build]\n[Something=Nice]", "Category", "Build")]
        [TestCase("\n[Category=Build]", "Category", "Build")]
        [TestCase("\nsome text[Category=Build]", "Category", "Build")]
        [TestCase("[Category=Build]\n", "Category", "Build")]
        [TestCase("[Category=Build]\n", "Category", "Build")]
        [TestCase("text\ntext[Category=Build]text\ntext", "Category", "Build")]
        public void ParseProperProperty(string body, string key, string expected)
        {
            _logic.GetTriageIssueProperty(key, body).Should().Be(expected);
        }

        [TestCase("", "")]
        [TestCase("", "Category")]
        [TestCase("[Category=]", "Category")]
        [TestCase("[Category=Build]", "Categ")]
        [TestCase("[Category=Build]", "Categorys")]
        [TestCase("[Category=Build]", "Something")]
        [TestCase("Category=Build]", "Category")]
        [TestCase("Category=Build", "Category")]
        [TestCase("[Category=Build", "Category")]
        public void NotProperProperty_ReturnsNull(string body, string key)
        {
            _logic.GetTriageIssueProperty(key, body).Should().BeNull();
        }

        [Test, AutoData]
        public void TriageEquals_By_Build_Record_Index_Only(TriageItem a, TriageItem b)
        {
            a.Equals(b).Should().BeFalse();

            a.BuildId = b.BuildId;
            a.Equals(b).Should().BeFalse();

            a.RecordId = b.RecordId;
            a.Equals(b).Should().BeFalse();

            a.BuildId = b.BuildId;
            a.Equals(b).Should().BeFalse();

            a.Index = b.Index;
            a.Equals(b).Should().BeTrue();
        }

        private static string GetTestPayload(string name)
        {
            Type thisClass = typeof(TimeLineIssueServiceLogicTests);
            Assembly asm = thisClass.Assembly;
            var resource = string.Format($"{thisClass.Namespace}.Files.{name}");
            using var stream = asm.GetManifestResourceStream(resource);
            using var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        }
    }
}
