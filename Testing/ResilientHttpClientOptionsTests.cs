using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Polly;

namespace Common.Helper.Http.Tests;

public sealed class ResilientHttpClientOptionsTests
{
    [Fact]
    public void Defaults_AreExpected()
    {
        var options = new ResilientHttpClientOptions();

        Assert.Null(options.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
        Assert.Empty(options.DefaultHeaders);
        Assert.True(options.Resilience.Enabled);
        Assert.True(options.Resilience.Retry.Enabled);
        Assert.Equal(3, options.Resilience.Retry.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(1), options.Resilience.Retry.Delay);
        Assert.Equal(DelayBackoffType.Exponential, options.Resilience.Retry.BackoffType);
        Assert.True(options.Resilience.Retry.UseJitter);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Resilience.Retry.MaxDelay);
        Assert.True(options.Resilience.CircuitBreaker.Enabled);
        Assert.Equal(0.5, options.Resilience.CircuitBreaker.FailureRatio);
        Assert.Equal(100, options.Resilience.CircuitBreaker.MinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(5), options.Resilience.CircuitBreaker.BreakDuration);
        Assert.True(options.Resilience.AttemptTimeout.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Resilience.AttemptTimeout.Timeout);
    }

    [Fact]
    public void Section_IsBound_FromConfiguration()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["HttpClients:TestApi:BaseAddress"] = "https://api.example.com/v1/",
            ["HttpClients:TestApi:Timeout"] = "00:00:07",
            ["HttpClients:TestApi:DefaultHeaders:Authorization"] = "Bearer secret",
            ["HttpClients:TestApi:DefaultHeaders:X-Trace"] = "trace-1",
            ["HttpClients:TestApi:Resilience:Enabled"] = "true",
            ["HttpClients:TestApi:Resilience:Retry:Enabled"] = "true",
            ["HttpClients:TestApi:Resilience:Retry:MaxRetryAttempts"] = "5",
            ["HttpClients:TestApi:Resilience:Retry:Delay"] = "00:00:00.200",
            ["HttpClients:TestApi:Resilience:Retry:MaxDelay"] = "00:00:10",
            ["HttpClients:TestApi:Resilience:Retry:UseJitter"] = "false",
            ["HttpClients:TestApi:Resilience:Retry:BackoffType"] = "Linear",
            ["HttpClients:TestApi:Resilience:Retry:RetryStatusCodes:0"] = "500",
            ["HttpClients:TestApi:Resilience:Retry:RetryStatusCodes:1"] = "503",
            ["HttpClients:TestApi:Resilience:CircuitBreaker:Enabled"] = "true",
            ["HttpClients:TestApi:Resilience:CircuitBreaker:FailureRatio"] = "0.25",
            ["HttpClients:TestApi:Resilience:CircuitBreaker:MinimumThroughput"] = "10",
            ["HttpClients:TestApi:Resilience:CircuitBreaker:SamplingDuration"] = "00:00:15",
            ["HttpClients:TestApi:Resilience:CircuitBreaker:BreakDuration"] = "00:01:00",
            ["HttpClients:TestApi:Resilience:CircuitBreaker:BreakStatusCodes:0"] = "502",
            ["HttpClients:TestApi:Resilience:AttemptTimeout:Enabled"] = "true",
            ["HttpClients:TestApi:Resilience:AttemptTimeout:Timeout"] = "00:00:02",
        });

        var options = configuration.GetSection("HttpClients:TestApi").Get<ResilientHttpClientOptions>();

        Assert.NotNull(options);
        Assert.Equal("https://api.example.com/v1/", options.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(7), options.Timeout);
        Assert.Equal("Bearer secret", options.DefaultHeaders["Authorization"]);
        Assert.Equal("trace-1", options.DefaultHeaders["X-Trace"]);

        Assert.True(options.Resilience.Retry.Enabled);
        Assert.Equal(5, options.Resilience.Retry.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(200), options.Resilience.Retry.Delay);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Resilience.Retry.MaxDelay);
        Assert.False(options.Resilience.Retry.UseJitter);
        Assert.Equal(DelayBackoffType.Linear, options.Resilience.Retry.BackoffType);
        Assert.NotNull(options.Resilience.Retry.RetryStatusCodes);
        Assert.Equal([500, 503], options.Resilience.Retry.RetryStatusCodes);

        Assert.Equal(0.25, options.Resilience.CircuitBreaker.FailureRatio);
        Assert.Equal(10, options.Resilience.CircuitBreaker.MinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(15), options.Resilience.CircuitBreaker.SamplingDuration);
        Assert.Equal(TimeSpan.FromMinutes(1), options.Resilience.CircuitBreaker.BreakDuration);
        Assert.NotNull(options.Resilience.CircuitBreaker.BreakStatusCodes);
        Assert.Equal([502], options.Resilience.CircuitBreaker.BreakStatusCodes);

        Assert.True(options.Resilience.AttemptTimeout.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(2), options.Resilience.AttemptTimeout.Timeout);
    }

    [Fact]
    public void DisabledFlags_AreBound()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Api:Resilience:Enabled"] = "false",
            ["Api:Resilience:Retry:Enabled"] = "false",
            ["Api:Resilience:CircuitBreaker:Enabled"] = "false",
            ["Api:Resilience:AttemptTimeout:Enabled"] = "false",
        });

        var options = configuration.GetSection("Api").Get<ResilientHttpClientOptions>();

        Assert.NotNull(options);
        Assert.False(options.Resilience.Enabled);
        Assert.False(options.Resilience.Retry.Enabled);
        Assert.False(options.Resilience.CircuitBreaker.Enabled);
        Assert.False(options.Resilience.AttemptTimeout.Enabled);
    }

    [Fact]
    public void MissingSection_ReturnsNull()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        var options = configuration.GetSection("Missing").Get<ResilientHttpClientOptions>();

        // Отсутствующая секция не создаёт экземпляр: значения по умолчанию берутся
        // из свойств ResilientHttpClientOptions при регистрации через DI.
        Assert.Null(options);
    }

    private static IConfigurationRoot BuildConfiguration(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
