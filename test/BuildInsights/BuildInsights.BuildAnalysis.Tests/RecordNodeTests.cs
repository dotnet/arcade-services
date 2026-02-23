using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using BuildInsights.BuildAnalysis;
using BuildInsights.BuildAnalysis.Models;
using NUnit.Framework;

namespace BuildInsights.BuildResultProcessor.Tests
{
    [TestFixture]
    public class RecordNodeTests
    {

        [Test]
        public void GetTimelineRecordsOrdererByTreeStructureTest()
        {
            var expected = new List<string> {"A", "AA", "AAA", "AAAA", "AAB", "AABA", "AB", "ABA", "ABAA", "ABB", "ABC"};
            IEnumerable<TimelineRecord> timeline = CreateTimeline();

            IEnumerable<TimelineRecord> result = RecordNode.GetTimelineRecordsOrdererByTreeStructure(timeline);
            result.Select(r => r.Name).Should().Equal(expected);
        }

        [Test]
        public void GetTimelineRecordsOrdererByTreeStructureNullOrderTest()
        {
            var expected = new List<string> {"A", "AA", "AAA", "AAAC", "AAAA", "AAAB"};
            IEnumerable<TimelineRecord> timeline = CreateTimelineWithNullOrder().ToList();

            IEnumerable<TimelineRecord> result = RecordNode.GetTimelineRecordsOrdererByTreeStructure(timeline);

            result.Select(r => r.Name).Should().Equal(expected);
        }

        [Test]
        public void NoParentsNoCrashesTest()
        {
            // Some timelines from canceled or abandoned builds have no root.

            List<TimelineRecord> timeline = new List<TimelineRecord>()
            {
                MockRecord("A", "B")
            };

            Action act = () => RecordNode.GetTimelineRecordsOrdererByTreeStructure(timeline);

            act.Should().NotThrow();
        }

        private IEnumerable<TimelineRecord> CreateTimeline()
        {
            /*Timeline:
             * A
             *  AA
             *    AAA
             *     AAAA
             *    AAB
             *     AABA
             *  AB
             *    ABA
             *     ABAA
             *    ABB
             *    ABC
             */

            var records = new List<TimelineRecord>
            {
                MockRecord("A", 0),
                MockRecord("AA", 1, "A"),
                MockRecord("AB", 2, "A"),
                MockRecord("AAA", 1, "AA"),
                MockRecord("AAB", 1, "AA"),
                MockRecord("ABA", 1, "AB"),
                MockRecord("ABB", 2, "AB"),
                MockRecord("ABC", 3, "AB"),
                MockRecord("AAAA", 1, "AAA"),
                MockRecord("AABA", 1, "AAB"),
                MockRecord("ABAA", 1, "ABA")
            };

            return records;
        }

        private IEnumerable<TimelineRecord> CreateTimelineWithNullOrder()
        {
            /*Timeline:
             * A
             *  AA
             *    AAA
             *     AAAA
             *     AAAB
             *     AAAC
             */

            var records = new List<TimelineRecord>
            {
                MockRecord("A", 0),
                MockRecord("AA", 1, "A"),
                MockRecord("AAA", 1, "AA"),
                MockRecord("AAAA", 1, "AAA"),
                MockRecord("AAAB", 2, "AAA"),
                MockRecord("AAAC", "AAA")
            };


            return records;
        }

        private static TimelineRecord MockRecord(string name, int order, string parent = null)
        {
            return new TimelineRecord(
                id: Guid.Parse($"00000000-0000-0000-0000-{name.PadLeft(12, '0')}"),
                name: name,
                order: order,
                parentId: parent != null
                    ? Guid.Parse($"00000000-0000-0000-0000-{parent.PadLeft(12, '0')}")
                    : (Guid?)null
            );
        }

        private static TimelineRecord MockRecord(string name, string parent = null)
        {
            return new TimelineRecord
            (
                id: Guid.Parse($"00000000-0000-0000-0000-{name.PadLeft(12, '0')}"),
                name: name,
                parentId: parent != null
                    ? Guid.Parse($"00000000-0000-0000-0000-{parent.PadLeft(12, '0')}")
                    : (Guid?)null
            );
        }
    }
}
