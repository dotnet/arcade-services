# Dependency Injection TestData Source Generation

Test code that integrate with dependency injected types require a significant amount of boilerplate
setup code.  In order to automate much of that code generation, we have created a source code generator.

### Input Code
```c#
/// For the boilderplate code to function, the test class must be in a namespace
namespace Example.Tests
{
    // For the boilerplate code to function, the test class must be "partial"
    public partial class ExampleTests
    {
        // A static class marked with this attribute will have it's static methods used to configure a TestData
        // class generated as a sibling of this class (in this example, inside ExampleTests)
        // The name of this class is not important, and can be anything (other than "TestData")  
        [TestDependencyInjectionSetup]
        public static class TestConfig
        {
            // Every static methd in the class that takes a first parameter of the type "IServiceCollection"
            // will be used to configure the test data.
            // Each parameter will be transfored into a "WithName" method on the builder
            // where the "Name" portion of the method is the name of the parameter
            // This means that the parameter names for all methods should be unique and desctriptive
            // Because this method returns a Func<IServiceProvider, SomeType>, the resulting TestData
            // class with have a member named "Name" where "Name" is the name of this method
            public static Func<IServiceProvider, IProductService> ExampleName(IServiceCollection services, string exampleValue)
            {
                // The body of each of these method is called once when "Build" is called on the TestData.Builder
                // object. Each method is invoked in order to set up the service collection
                services.AddSingleton(new MockService(value));
    
                return s => s.GetRequiredService<MockService>();
            }
    
            // A method with no parameters will not generate any "With" members on the TestData.Builder
            // type, but can still be used to return useful types from the collection on the TestData object
            public static Func<IServiceProvider, MockServiceWithNoParameters> Parameterless(IServiceCollection services)
            {
                services.AddSingleton(s => new MockServiceWithNoParameters(s.GetRequiredService<MockDefaultService>().Value + ":Other"));
    
                return s => s.GetRequiredService<MockServiceWithNoParameters>();
            }
            
            // A method with a void return value will not create a member on the TestData object
            // but can still be used to configured defaults, or read in parameters to configure mocks
            public static void DefaultSetup(IServiceCollection services)
            {
                services.AddSingleton(new MockDefaultService("Default"));
            }
            
            // This attribute causes a single "With" method to be generated to set all parameters at together
            // for cases where it doesn't make sense to configure them individually.
            // In this case, the method will be "With" + the name of this method
            [ConfigureAllParameters]
            public static void Func<IServiceProvider, MockServiceWithConcat> Concatenated(
                IServiceCollection services,
                string a,
                string b)
            {
                services.AddSingleton(new MockServiceWithConcat(a + " + " + b));
    
                return s => s.GetRequiredService<MockServiceWithConcat>();
            } 
        }
    
        public void Test()
        {
            // The resulting TestData is IDiposable, which will clean up all services built for the test
            using TestData t = TestData.Default
                // This controls the "exampleValue" parameters to the GetExampleName method
                // the named parameters aren't necessary, they are only for illustrative purposes
                .WithExampleValue(exampleValue: "test-value") 
                .WithConcatenated(a: "AAA", b: "BBB")
                .Build();
            
            // This returns the value returned by the ExampleName    
            t.ExampleName.Value.Should().Be("test-value");
            t.Parameterless.Value.Should().Be("Default:Other");
            t.Concatenated.Value.Should().Be("AAA + BBB");
        }
    
        public class MockService : IProductService
        {
            public MockService(string value)
            {
                Value = value;
            }
    
            public string Value { get; }
        }
    
        public class MockDefaultService : MockService
        {
            // ...
        }
        
        public class MockServiceWithNoParameters : MockService
        {
            // ...
        }
        public class MockServiceWithConcat : MockService
        {
            // ...
        }
    }
}
```

## Generated Code Reference
```c#
/// For the boilderplate code to function, the test class must be in a namespace
namespace Example.Tests
{
    // For the boilerplate code to function, the test class must be "partial"
    public partial class ExampleTests
    {
        private class TestData : IDisposable, IAsyncDisposable
        {
            void Dispose {}
            ValueTask DisposeAsync {}
            
            IProductService ExampleName { get; }
            MockServiceWithNoParameters Parameterless { get; }
            MockServiceWithConcat Concatenated { get; }
            
            public class Builder
            {
                public static Default { get; }
                
                public Builder WithExampleValue(string exampleValue) { }
                public Builder WithConcatenated(string a, string b) { }
                
                public TestData Build() { }
            }
        }
    }
}
```
