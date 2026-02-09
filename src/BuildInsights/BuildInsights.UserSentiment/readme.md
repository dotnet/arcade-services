## Summary
Use this library to inject "User Sentiment" code into your feature.

## Usage
The primary usage scenario is with dependency injection to inject the `SentimentInjection`
class, and then get the appropriate code snippet for your target language.

### Dependency Injection Setup
Add this code to app startup when configurting an `IServiceCollection`
```csharp
serviceCollection.AddUserSentiment(o => o.Host = hostReadFromConfiguration);
```

### Usage in service
```csharp
public class MyFeature {
  private readonly FeatureSentimentInjection _sentiment;

  public MyFeature(SentimentInjection sentiment) {
    _sentiment = sentiment.ForFeature(SentimentFeature.MyFeature);
  }

  public string CreateGitHubCommentBody(int someParameter) {
    var injector = _sentiment
      .WithProperty("SomePropertyKey", someParameter.ToString());
    string userSentimentSegment = injector.GetMarkdown();

    return @$"
Body of the comment which will be followed by the user sentiment buttons.

{userSentimentSegment}
";
  }
}
```

## Registering new feature
New features simply need to be added to the `SentimentFeature` enumeration