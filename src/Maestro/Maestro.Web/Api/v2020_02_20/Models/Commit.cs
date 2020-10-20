using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Maestro.Web.Api.v2020_02_20.Models
{
    public class Commit
    {
        public Commit(string author, string sha, string message)
        {
            Author = author;
            Sha = sha;
            Message = message ?? string.Empty;
        }

        public string Author { get; }
        public string Sha { get; }
        public string Message { get; }
    }
}
