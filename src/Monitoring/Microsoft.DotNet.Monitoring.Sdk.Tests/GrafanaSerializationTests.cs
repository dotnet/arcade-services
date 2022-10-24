using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.DotNet.Monitoring.Sdk.Tests;

internal class GrafanaSerializationTests
{
    /*
     *  {
     *    "dashboard": {
     *      "panels": [{
     *        "targets": [{
     *              "azureMonitor": {
     *                "resourceGroup": "value1",
     *                "resourceName": "value2",
     *                "duplicateTest": "value1"
     *            }}]}, {
     *        "targets": [{
     *            "azureLogAnalytics": {
     *              "resource": "value1"
     *            }}]}]}
     *  }
     */
    readonly JObject _valueReplacementDashboard = new JObject
    {
        {
            "dashboard", new JObject { {
                "panels", new JArray {
                    new JObject { {
                        "targets", new JArray {
                            new JObject { {
                                "azureMonitor", new JObject {
                                    { "resourceGroup", "value1" },
                                    { "resourceName", "value2" },
                                    { "duplicateTest", "value1" }
                                }}}}}},
                    new JObject { {
                        "targets", new JArray {
                            new JObject { {
                                "azureLogAnalytics", new JObject {
                                    { "resource", "value1" },
                                }}}}}},
                }}}
        }
    };

    private readonly JObject _dashboardWithParameters = new JObject
    {
        {
            "dashboard", new JObject { {
                "panels", new JArray {
                    new JObject { {
                        "targets", new JArray {
                            new JObject { {
                                "azureMonitor", new JObject {
                                    { "resourceGroup", "[Parameter(MyNamedValue1Parameter)]" },
                                    { "resourceName", "[Parameter(MyNamedValue2Parameter)]" },
                                    { "duplicateTest", "[Parameter(MyNamedValue1Parameter)]" }
                                }}}}}},
                    new JObject { {
                        "targets", new JArray {
                            new JObject { {
                                "azureLogAnalytics", new JObject {
                                    { "resource", "[Parameter(MyNamedValue1Parameter)]" },
                                }}}}}},
                }}}
        }
    };

    private readonly Parameter[] _parameters = new Parameter[] {
        new Parameter()
        {
            Name = "MyNamedValue1Parameter",
            Values = new Dictionary<string, string>() {
                { "Staging", "value1" },
                { "Production", "productionValue1" }
            }
        },
        new Parameter()
        {
            Name = "MyNamedValue2Parameter",
            Values = new Dictionary<string, string>() {
                { "Staging", "value2" },
                { "Production", "productionValue2" }
            }
        }
    };

    private readonly string[] _environments = { "Production", "Staging" };

    [Test]
    public void NoExtantParametersTest()
    {
        List<Parameter> parameters = new List<Parameter>();

        string environment = "Staging";

        JObject parameterizedDashboard = GrafanaSerialization.ParameterizeDashboard(_valueReplacementDashboard, parameters, _environments, environment);

        parameters.Should()
            .HaveCount(2);
        parameters.Select(p => p.Values.Keys).Should()
            .AllBeEquivalentTo(_environments);
        parameters.Select(p => p.Values[environment]).Should()
            .BeEquivalentTo(_parameters
                .SelectMany(p => p.Values
                    .Where(kvp => kvp.Key == environment)
                    .Select(kvp => kvp.Value)));

        parameterizedDashboard.SelectToken("$.dashboard.panels[0].targets[0].azureMonitor.resourceGroup")?.Value<string>()
            .Should().NotBeNull().And.StartWith("[parameter(");
        parameterizedDashboard.SelectToken("$.dashboard.panels[0].targets[0].azureMonitor.resourceName")?.Value<string>()
            .Should().NotBeNull().And.StartWith("[parameter(");
        parameterizedDashboard.SelectToken("$.dashboard.panels[1].targets[0].azureLogAnalytics.resource")?.Value<string>()
            .Should().NotBeNull().And.StartWith("[parameter(");
    }

    [Test]
    public void ExtantParametersTest()
    {
        string environment = "Staging";
        List<Parameter> parameters = new List<Parameter>(_parameters);

        JObject parameterizedDashboard = GrafanaSerialization.ParameterizeDashboard(_valueReplacementDashboard, parameters, _environments, environment);

        // Expect that the parameters list has not changed
        parameters.Select(p => p.Values[environment]).Should()
            .BeEquivalentTo(_parameters
                .SelectMany(p => p.Values
                    .Where(kvp => kvp.Key == environment)
                    .Select(kvp => kvp.Value)));

        // Expect that the dashboard values are replaced with the preset parameter names
        parameterizedDashboard.SelectToken("$.dashboard.panels[0].targets[0].azureMonitor.resourceGroup")?.Value<string>()
            .Should().NotBeNull()
            .And.StartWith("[parameter(MyNamedValue1Parameter)]");
        parameterizedDashboard.SelectToken("$.dashboard.panels[0].targets[0].azureMonitor.resourceName")?.Value<string>()
            .Should().NotBeNull()
            .And.StartWith("[parameter(MyNamedValue2Parameter)]");
        parameterizedDashboard.SelectToken("$.dashboard.panels[1].targets[0].azureLogAnalytics.resource")?.Value<string>()
            .Should().NotBeNull()
            .And.StartWith("[parameter(MyNamedValue1Parameter)]");
    }

    [Test]
    public void DeparamaterizeTest()
    {
        string environment = "Staging";

        JObject deparameterizedDashboard = GrafanaSerialization.DeparameterizeDashboard(_dashboardWithParameters, _parameters, environment);

        deparameterizedDashboard.SelectToken("$.dashboard.panels[0].targets[0].azureMonitor.resourceGroup")?.Value<string>()
            .Should().Be(_parameters[0].Values[environment]);
        deparameterizedDashboard.SelectToken("$.dashboard.panels[0].targets[0].azureMonitor.resourceName")?.Value<string>()
            .Should().Be(_parameters[1].Values[environment]);
        deparameterizedDashboard.SelectToken("$.dashboard.panels[0].targets[0].azureMonitor.duplicateTest")?.Value<string>()
            .Should().Be(_parameters[0].Values[environment]);
        deparameterizedDashboard.SelectToken("$.dashboard.panels[1].targets[0].azureLogAnalytics.resource")?.Value<string>()
            .Should().Be(_parameters[0].Values[environment]);
    }

    [Test]
    public void Deparameterize_ThrowIfUnknownNameTest()
    {
        string environment = "Staging";

        // Cause exception by including only some of the definitions used in the dashboard
        IEnumerable<Parameter> parameters = _parameters.Take(1);

        Action act = () => GrafanaSerialization.DeparameterizeDashboard(_dashboardWithParameters, parameters, environment);

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Deparameterize_ThrowIfUnknownEnvironmentTest()
    {
        // Cause exception by specifying environment not used in the definition list
        string environment = "MadeUpEnvironment";

        Action act = () => GrafanaSerialization.DeparameterizeDashboard(_dashboardWithParameters, _parameters, environment);

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Deparameterize_ThrowIfPlaceholderPresentTest()
    {
        // Cause exception by specifying a parameter with the placeholder string in it.
        string environment = _environments[0];
        List<Parameter> parameters = new List<Parameter>(_parameters);
        parameters.Add(new Parameter() { 
            Name = "PLACEHOLDER:12345678-1234-1234-1234-1234567890AB", 
            Values = new Dictionary<string, string>() {
                { environment, "MadeUpValue" } 
            } 
        });

        Action act = () => GrafanaSerialization.DeparameterizeDashboard(_dashboardWithParameters, parameters, environment);

        act.Should().Throw<ArgumentException>();
    }
}
