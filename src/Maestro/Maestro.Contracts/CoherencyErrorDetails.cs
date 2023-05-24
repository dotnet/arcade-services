using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Maestro.Contracts;

[DataContract]
public class CoherencyErrorDetails
{
    [DataMember]
    public string Error { get; set; }

    [DataMember]
    public IEnumerable<string> PotentialSolutions { get; set; }
}
