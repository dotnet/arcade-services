namespace Microsoft.DotNet.Kusto
{
    public class KustoParameter
    {
        public KustoParameter(string name, object value, KustoDataType type)
        {
            Name = name;
            Type = type;
            Value = value;
        }

        public string Name { get; }
        public KustoDataType Type { get; }
        public object Value { get; set; }
    }
}
