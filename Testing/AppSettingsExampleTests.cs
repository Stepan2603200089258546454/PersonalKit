using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Polly;

namespace Common.Helper.Http.Tests;

/// <summary>
/// Проверяет, что пример настроек из корня библиотеки (appsettings.example.json)
/// читается провайдером конфигурации вместе с комментариями и корректно привязывается.
/// </summary>
public sealed class AppSettingsExampleTests
{
    [Fact]
    public void GitHubExample_BindsAllSettings()
    {
        var options = LoadExampleSection("GitHub");

        Assert.Equal("https://api.github.com/", options.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
        Assert.Equal("application/vnd.github+json", options.DefaultHeaders["Accept"]);
        Assert.Equal("PersonalKit", options.DefaultHeaders["User-Agent"]);
        Assert.Equal("personal-kit", options.DefaultHeaders["X-Request-Source"]);

        Assert.True(options.Resilience.Enabled);
        Assert.True(options.Resilience.Retry.Enabled);
        Assert.Equal(3, options.Resilience.Retry.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(1), options.Resilience.Retry.Delay);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Resilience.Retry.MaxDelay);
        Assert.Equal(DelayBackoffType.Exponential, options.Resilience.Retry.BackoffType);
        Assert.True(options.Resilience.Retry.UseJitter);
        Assert.NotNull(options.Resilience.Retry.RetryStatusCodes);
        Assert.Equal([408, 429, 500, 502, 503, 504], options.Resilience.Retry.RetryStatusCodes);

        Assert.True(options.Resilience.CircuitBreaker.Enabled);
        Assert.Equal(0.5, options.Resilience.CircuitBreaker.FailureRatio);
        Assert.Equal(100, options.Resilience.CircuitBreaker.MinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Resilience.CircuitBreaker.SamplingDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), options.Resilience.CircuitBreaker.BreakDuration);
        Assert.NotNull(options.Resilience.CircuitBreaker.BreakStatusCodes);
        Assert.Equal([408, 429, 500, 502, 503, 504], options.Resilience.CircuitBreaker.BreakStatusCodes);

        Assert.True(options.Resilience.AttemptTimeout.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Resilience.AttemptTimeout.Timeout);
    }

    [Fact]
    public void GitHubExample_PassesValidation()
    {
        var options = LoadExampleSection("GitHub");

        var result = new ResilientHttpClientOptionsValidator().Validate("GitHub", options);

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void MinimalExample_UsesDefaults()
    {
        var options = LoadExampleSection("InternalApi");

        Assert.Equal("https://internal.example.local/api/", options.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(15), options.Timeout);
        Assert.Empty(options.DefaultHeaders);

        Assert.True(options.Resilience.Enabled);
        Assert.Equal(3, options.Resilience.Retry.MaxRetryAttempts);
        Assert.Equal(DelayBackoffType.Exponential, options.Resilience.Retry.BackoffType);
        Assert.True(options.Resilience.Retry.UseJitter);
        Assert.True(options.Resilience.CircuitBreaker.Enabled);
        Assert.True(options.Resilience.AttemptTimeout.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Resilience.AttemptTimeout.Timeout);
    }

    [Fact]
    public void MinimalExample_PassesValidation()
    {
        var options = LoadExampleSection("InternalApi");

        var result = new ResilientHttpClientOptionsValidator().Validate("InternalApi", options);

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    private static ResilientHttpClientOptions LoadExampleSection(string clientName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.example.json");
        Assert.True(File.Exists(path), $"Не найден пример настроек: {path}");

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(path, optional: false, reloadOnChange: false)
            .Build();

        var options = configuration.GetSection($"HttpClients:{clientName}").Get<ResilientHttpClientOptions>();

        Assert.NotNull(options);
        return options;
    }
}
