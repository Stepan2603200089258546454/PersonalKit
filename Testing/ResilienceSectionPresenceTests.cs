using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Common.Helper.Http.Tests;

public sealed class ResilienceSectionPresenceTests
{
    [Fact]
    public void MissingResilienceSection_DisablesResilience()
    {
        var options = new ResilientHttpClientOptions();

        ResilienceSectionPresence.Apply(options, Section(new Dictionary<string, string?>()));

        Assert.False(options.Resilience.Enabled);
    }

    [Fact]
    public void ExistingResilienceSectionWithoutStrategies_DisablesAllStrategies()
    {
        var options = new ResilientHttpClientOptions();

        ResilienceSectionPresence.Apply(options, Section(new Dictionary<string, string?>
        {
            ["HttpClients:Api:Resilience:Enabled"] = "true",
        }));

        Assert.True(options.Resilience.Enabled);
        Assert.False(options.Resilience.Retry.Enabled);
        Assert.False(options.Resilience.CircuitBreaker.Enabled);
        Assert.False(options.Resilience.AttemptTimeout.Enabled);
    }

    [Fact]
    public void OnlyRetrySection_EnablesRetryAndDisablesOtherStrategies()
    {
        var options = new ResilientHttpClientOptions();

        ResilienceSectionPresence.Apply(options, Section(new Dictionary<string, string?>
        {
            ["HttpClients:Api:Resilience:Retry:MaxRetryAttempts"] = "2",
        }));

        Assert.True(options.Resilience.Enabled);
        Assert.True(options.Resilience.Retry.Enabled);
        Assert.False(options.Resilience.CircuitBreaker.Enabled);
        Assert.False(options.Resilience.AttemptTimeout.Enabled);
    }

    [Fact]
    public void SectionWithExplicitEnabledFalse_StaysDisabled()
    {
        var options = new ResilientHttpClientOptions();

        ResilienceSectionPresence.Apply(options, Section(new Dictionary<string, string?>
        {
            ["HttpClients:Api:Resilience:Retry:Enabled"] = "false",
            ["HttpClients:Api:Resilience:CircuitBreaker:Enabled"] = "false",
            ["HttpClients:Api:Resilience:AttemptTimeout:Enabled"] = "false",
        }));

        Assert.True(options.Resilience.Enabled);
        Assert.False(options.Resilience.Retry.Enabled);
        Assert.False(options.Resilience.CircuitBreaker.Enabled);
        Assert.False(options.Resilience.AttemptTimeout.Enabled);
    }

    [Fact]
    public void ExplicitParentDisabled_IsNotOverridden()
    {
        var options = new ResilientHttpClientOptions();

        ResilienceSectionPresence.Apply(options, Section(new Dictionary<string, string?>
        {
            ["HttpClients:Api:Resilience:Enabled"] = "false",
            ["HttpClients:Api:Resilience:Retry:MaxRetryAttempts"] = "2",
        }));

        Assert.False(options.Resilience.Enabled);
        Assert.True(options.Resilience.Retry.Enabled);
    }

    [Fact]
    public void EmptyJsonObject_IsTreatedAsMissing()
    {
        const string json = """{"HttpClients":{"Api":{"Resilience":{}}}}""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        var options = new ResilientHttpClientOptions();

        ResilienceSectionPresence.Apply(options, configuration.GetSection("HttpClients:Api:Resilience"));

        Assert.False(options.Resilience.Enabled);
    }

    [Fact]
    public void NullOptions_Throw()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ResilienceSectionPresence.Apply(null!, Section(new Dictionary<string, string?>())));
    }

    [Fact]
    public void NullSection_Throw()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ResilienceSectionPresence.Apply(new ResilientHttpClientOptions(), null!));
    }

    private static IConfigurationSection Section(IDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return configuration.GetSection("HttpClients:Api:Resilience");
    }
}
