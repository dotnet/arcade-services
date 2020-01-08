namespace Microsoft.DotNet.Kusto
{
    public class KustoParameter
    {
        public KustoParameter(string name, string type)
        {
            Name = name;
            Type = type;
        }

        public KustoParameter(string name, string type, object value)
        {
            Name = name;
            Type = type;
            Value = value;
        }

        public string Name { get; }
        public string Type { get; }

        public object Value { get; set; }

        public bool IsValid()
        {
            if (Value is string)
            {
                if (Type != KustoDataTypes.String) return false;
            }

            return true;
        }
    }
}
