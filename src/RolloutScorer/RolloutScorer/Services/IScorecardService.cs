using RolloutScorer.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace RolloutScorer.Services
{
    public interface IScorecardService
    {
        Task<Scorecard> CreateScorecardAsync(Models.RolloutScorer rolloutScorer);
        Task<Scorecard> ParseScorecardFromCsvAsync(string filePath, Config config);
    }
}
