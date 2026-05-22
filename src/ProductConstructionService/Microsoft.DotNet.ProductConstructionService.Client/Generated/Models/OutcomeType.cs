using System;
using System.Collections.Immutable;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public enum OutcomeType
    {
        [EnumMember(Value = "updated")]
        Updated,
        [EnumMember(Value = "noUpdate")]
        NoUpdate,
        [EnumMember(Value = "notUpdatable")]
        NotUpdatable,
        [EnumMember(Value = "failure")]
        Failure,
        [EnumMember(Value = "userError")]
        UserError,
        [EnumMember(Value = "hasConflict")]
        HasConflict,
        [EnumMember(Value = "rescheduled")]
        Rescheduled,
    }
}
