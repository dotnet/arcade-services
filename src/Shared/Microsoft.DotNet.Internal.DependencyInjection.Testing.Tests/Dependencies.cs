namespace Microsoft.DotNet.Internal.DependencyInjection.Testing.Tests
{
    internal interface ISimple
    {
        string SimpleValue { get; }
    }

    internal class Simple : ISimple
    {
        public string SimpleValue => "Simple";
    }

    internal interface IWithValue
    {
        string Value { get; }
    }

    internal class WithValue : IWithValue
    {
        public WithValue(string value)
        {
            Value = $"WV-{value}";
        }

        public string Value { get; }
    }

    internal class NeedsSimple
    {
        private readonly ISimple _a;

        public NeedsSimple(ISimple a)
        {
            _a = a;
        }

        public string Value => $"NeedsSimple : {_a.SimpleValue}";
    }

    internal class NeedsBoth
    {
        private readonly ISimple _a;
        private readonly IWithValue _b;

        public NeedsBoth(ISimple a, IWithValue b)
        {
            _a = a;
            _b = b;
        }

        public string Value => $"NeedsBoth : {_a.SimpleValue} : {_b.Value}";
    }

    internal class NeedsValue
    {
        private readonly IWithValue _b;

        public NeedsValue(IWithValue b)
        {
            _b = b;
        }

        public string Value => $"NeedsValue : {_b.Value}";
    }

    internal class NeedsSimpleOptional
    {
        private readonly ISimple _a;

        public NeedsSimpleOptional()
        {
        }

        public NeedsSimpleOptional(ISimple a)
        {
            _a = a;
        }

        public string Value => $"NeedsSimpleOptional : {_a?.SimpleValue ?? "<null>"}";
    }

    internal class NeedsAny
    {
        private readonly ISimple _a;
        private readonly IWithValue _b;

        public NeedsAny()
        {
        }
        
        public NeedsAny(ISimple a)
        {
            _a = a;
        }
        public NeedsAny(IWithValue b)
        {
            _b = b;
        }

        public NeedsAny(ISimple a, IWithValue b)
        {
            _a = a;
            _b = b;
        }

        public string Value => $"NeedsAny : {(_a?.SimpleValue ?? "<null>")} : {(_b?.Value ?? "<null>")}";
    }

    internal class NeedsSome
    {
        private readonly ISimple _a;
        private readonly IWithValue _b;
        
        public NeedsSome(ISimple a)
        {
            _a = a;
        }
        public NeedsSome(IWithValue b)
        {
            _b = b;
        }

        public NeedsSome(ISimple a, IWithValue b)
        {
            _a = a;
            _b = b;
        }

        public string Value => $"NeedsSome : {(_a?.SimpleValue ?? "<null>")} : {(_b?.Value ?? "<null>")}";
    }
}
