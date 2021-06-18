using RolloutScorer.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace RolloutScorer.Services
{
    public interface IPersistenceService
    {
        Task<int> WriteScorecardToCSV(Scorecard scorecard, string filePath);
    }
}
