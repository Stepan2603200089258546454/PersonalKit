using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Common.Helper.Http.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Common.Helper.Http.Tests;

public interface ITestClient
{
    HttpClient HttpClient { get; }

    Task<HttpResponseMessage> GetAsync(string path);
}

public sealed class TestClient : ITestClient
{
    public TestClient(HttpClient httpClient)
    {
        HttpClient = httpClient;
    }

    public HttpClient HttpClient { get; }

    public Task<HttpResponseMessage> GetAsync(string path) => HttpClient.GetAsync(path);
}

public sealed class ServiceCollectionExtensionsTests
{
    private const string Section = "HttpClients:TestApi";

    [Fact]
    public async Task RegistersTypedClient_AndSendsRequest()
    {
        var inner = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        var configuration = BuildConfiguration(new Dictionary<string, string?>()
        {
            [$"{Section}:BaseAddress"] = "https://config.test/",
            [$"{Section}:DefaultHeaders:X-Config"] = "yes",
            [$"{Section}:Resilience:Retry:MaxRetryAttempts"] = "0",
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddResilientHttpClient<ITestClient, TestClient>(configuration, Section)
            .ConfigurePrimaryHttpMessageHandler(() => inner);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITestClient>();

        using var response = await client.GetAsync("api/items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://config.test/api/items", inner.Requests[0].RequestUri!.ToString());
        Assert.Equal(["yes"], inner.Requests[0].Headers["X-Config"]);
    }

    [Fact]
    public void HttpClientTimeout_IsInfinite_BecauseHandlerControlsTimeout()
    {
        var inner = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        var configuration = BuildConfiguration(new Dictionary<string, string?>()
        {
            [$"{Section}:BaseAddress"] = "https://config.test/",
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddResilientHttpClient<ITestClient, TestClient>(configuration, Section)
            .ConfigurePrimaryHttpMessageHandler(() => inner);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITestClient>();

        Assert.Equal(Timeout.InfiniteTimeSpan, client.HttpClient.Timeout);
    }

    [Fact]
    public void Options_AreBoundForClientName()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>()
        {
            [$"{Section}:BaseAddress"] = "https://config.test/",
            [$"{Section}:Timeout"] = "00:00:11",
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddResilientHttpClient<ITestClient, TestClient>(configuration, Section)
            .ConfigurePrimaryHttpMessageHandler(() => RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<ResilientHttpClientOptions>>();

        var options = monitor.Get(typeof(ITestClient).FullName!);

        Assert.Equal("https://config.test/", options.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(11), options.Timeout);
    }

    [Fact]
    public async Task ConfigurationHasPriorityOverConfigureAction()
    {
        var inner = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        var configuration = BuildConfiguration(new Dictionary<string, string?>()
        {
            [$"{Section}:BaseAddress"] = "https://config.test/",
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddResilientHttpClient<ITestClient, TestClient>(
                configuration,
                Section,
                options => options.BaseAddress = "https://default.test/")
            .ConfigurePrimaryHttpMessageHandler(() => inner);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITestClient>();

        using var response = await client.GetAsync("api/items");

        Assert.Equal("https://config.test/api/items", inner.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task ConfigureAction_AppliesWhenConfigurationIsEmpty()
    {
        var inner = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddResilientHttpClient<ITestClient, TestClient>(
                configuration,
                Section,
                options =>
                {
                    options.BaseAddress = "https://default.test/";
                    options.DefaultHeaders["X-Default"] = "1";
                })
            .ConfigurePrimaryHttpMessageHandler(() => inner);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITestClient>();

        using var response = await client.GetAsync("api/items");

        Assert.Equal("https://default.test/api/items", inner.Requests[0].RequestUri!.ToString());
        Assert.Equal(["1"], inner.Requests[0].Headers["X-Default"]);
    }

    [Fact]
    public async Task HotReload_ThroughConfiguration_ChangesBaseAddressAndHeaders()
    {
        var configuration = new ReloadableConfiguration(new Dictionary<string, string?>
        {
            [$"{Section}:BaseAddress"] = "https://first.test/",
            [$"{Section}:DefaultHeaders:X-Generation"] = "one",
            [$"{Section}:Resilience:Retry:MaxRetryAttempts"] = "0",
        });

        var inner = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);

        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddResilientHttpClient<ITestClient, TestClient>(configuration.Configuration, Section)
            .ConfigurePrimaryHttpMessageHandler(() => inner);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITestClient>();
        var clientName = typeof(ITestClient).FullName!;

        using var first = await client.GetAsync("api/items");

        configuration.Reload(new Dictionary<string, string?>
        {
            [$"{Section}:BaseAddress"] = "https://second.test/v2/",
            [$"{Section}:DefaultHeaders:X-Generation"] = "two",
            [$"{Section}:Resilience:Retry:MaxRetryAttempts"] = "0",
        });

        var monitor = provider.GetRequiredService<IOptionsMonitor<ResilientHttpClientOptions>>();
        await WaitUntilAsync(
            () => monitor.Get(clientName).BaseAddress == "https://second.test/v2/",
            TimeSpan.FromSeconds(5));

        using var second = await client.GetAsync("api/items");

        Assert.Equal("https://first.test/api/items", inner.Requests[0].RequestUri!.ToString());
        Assert.Equal("https://second.test/v2/api/items", inner.Requests[1].RequestUri!.ToString());
        Assert.Equal(["two"], inner.Requests[1].Headers["X-Generation"]);
    }

    [Fact]
    public async Task NamedClient_IsRegistered()
    {
        var inner = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        var configuration = BuildConfiguration(new Dictionary<string, string?>()
        {
            ["HttpClients:Named:BaseAddress"] = "https://named.test/",
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddResilientHttpClient("named-client", configuration, "HttpClients:Named")
            .ConfigurePrimaryHttpMessageHandler(() => inner);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("named-client");

        using var response = await client.GetAsync("api/items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://named.test/api/items", inner.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task InvalidConfiguration_FailsValidationOnFirstUse()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>()
        {
            [$"{Section}:BaseAddress"] = "not-an-uri",
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddResilientHttpClient<ITestClient, TestClient>(configuration, Section)
            .ConfigurePrimaryHttpMessageHandler(() => RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITestClient>();

        await Assert.ThrowsAsync<OptionsValidationException>(() => client.GetAsync("api/items"));
    }

    [Fact]
    public async Task RootRelativeUri_WorksThroughHttpClient()
    {
        var inner = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        var configuration = BuildConfiguration(new Dictionary<string, string?>()
        {
            [$"{Section}:BaseAddress"] = "https://config.test/v1/",
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddResilientHttpClient<ITestClient, TestClient>(configuration, Section)
            .ConfigurePrimaryHttpMessageHandler(() => inner);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITestClient>();

        using var response = await client.GetAsync("/root/items");

        Assert.Equal("https://config.test/root/items", inner.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task AbsoluteUri_IsNotRewrittenByHttpClient()
    {
        var inner = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        var configuration = BuildConfiguration(new Dictionary<string, string?>()
        {
            [$"{Section}:BaseAddress"] = "https://config.test/",
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddResilientHttpClient<ITestClient, TestClient>(configuration, Section)
            .ConfigurePrimaryHttpMessageHandler(() => inner);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITestClient>();

        using var response = await client.GetAsync("https://absolute.test/items");

        Assert.Equal("https://absolute.test/items", inner.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public void HttpClientBaseAddress_IsDynamicPlaceholder()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>()
        {
            [$"{Section}:BaseAddress"] = "https://config.test/",
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddResilientHttpClient<ITestClient, TestClient>(configuration, Section)
            .ConfigurePrimaryHttpMessageHandler(() => RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITestClient>();

        Assert.Equal(ResilientHttpMessageHandler.DynamicBaseAddress, client.HttpClient.BaseAddress);
    }

    [Fact]
    public async Task FluentRequest_UsesConfiguredBaseAddressAndRetry()
    {
        var inner = new RecordingHttpMessageHandler((index, _, _) =>
            Task.FromResult(index < 2 ? Responses.Status(HttpStatusCode.InternalServerError) : Responses.Ok()));

        var configuration = BuildConfiguration(new Dictionary<string, string?>()
        {
            [$"{Section}:BaseAddress"] = "https://config.test/",
            [$"{Section}:Resilience:Retry:MaxRetryAttempts"] = "2",
            [$"{Section}:Resilience:Retry:Delay"] = "00:00:00.001",
            [$"{Section}:Resilience:Retry:MaxDelay"] = "00:00:00.010",
            [$"{Section}:Resilience:Retry:UseJitter"] = "false",
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddResilientHttpClient<ITestClient, TestClient>(configuration, Section)
            .ConfigurePrimaryHttpMessageHandler(() => inner);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITestClient>();

        using var response = await client.HttpClient.NewRequest("api/items").SendAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.CallCount);
        Assert.Equal("https://config.test/api/items", inner.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public void NullServices_Throw()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        Assert.Throws<ArgumentNullException>(() =>
            ServiceCollectionExtensions.AddResilientHttpClient<ITestClient, TestClient>(
                null!,
                configuration,
                Section));
    }

    [Fact]
    public void NullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
            services.AddResilientHttpClient<ITestClient, TestClient>(null!, Section));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptySection_Throws(string? section)
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        Assert.ThrowsAny<ArgumentException>(() =>
            services.AddResilientHttpClient<ITestClient, TestClient>(configuration, section!));
    }

    [Fact]
    public void NullClientName_Throws()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        Assert.ThrowsAny<ArgumentException>(() =>
            services.AddResilientHttpClient(null!, configuration, Section));
    }

    [Fact]
    public void EmptyNamedSection_Throws()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        Assert.ThrowsAny<ArgumentException>(() =>
            services.AddResilientHttpClient("named", configuration, " "));
    }

    [Fact]
    public async Task NamedClients_UseTheirOwnSettings()
    {
        var inner = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        var configuration = BuildConfiguration(new Dictionary<string, string?>()
        {
            ["HttpClients:First:BaseAddress"] = "https://first.test/",
            ["HttpClients:Second:BaseAddress"] = "https://second.test/",
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddResilientHttpClient("first-client", configuration, "HttpClients:First")
            .ConfigurePrimaryHttpMessageHandler(() => inner);
        services
            .AddResilientHttpClient("second-client", configuration, "HttpClients:Second")
            .ConfigurePrimaryHttpMessageHandler(() => inner);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var first = await factory.CreateClient("first-client").GetAsync("api/items");
        using var second = await factory.CreateClient("second-client").GetAsync("api/items");

        Assert.Equal("https://first.test/api/items", inner.Requests[0].RequestUri!.ToString());
        Assert.Equal("https://second.test/api/items", inner.Requests[1].RequestUri!.ToString());
    }

    [Fact]
    public void NamedClient_OptionsAreBoundToClientName()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>()
        {
            ["HttpClients:Named:BaseAddress"] = "https://named.test/",
            ["HttpClients:Named:Timeout"] = "00:00:11",
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddResilientHttpClient("named-client", configuration, "HttpClients:Named")
            .ConfigurePrimaryHttpMessageHandler(() => RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<ResilientHttpClientOptions>>();

        var options = monitor.Get("named-client");

        Assert.Equal("https://named.test/", options.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(11), options.Timeout);
    }

    [Fact]
    public void OptionsValidator_IsRegisteredOnceForAllClients()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddResilientHttpClient("first", configuration, "HttpClients:First")
            .ConfigurePrimaryHttpMessageHandler(() => RecordingHttpMessageHandler.Status(HttpStatusCode.OK));
        services.AddResilientHttpClient("second", configuration, "HttpClients:Second")
            .ConfigurePrimaryHttpMessageHandler(() => RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var provider = services.BuildServiceProvider();
        var validators = provider.GetServices<IValidateOptions<ResilientHttpClientOptions>>().ToArray();

        Assert.Single(validators);
    }

    [Fact]
    public async Task MissingSection_UsesDefaults_RelativeRequestThrows()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddResilientHttpClient<ITestClient, TestClient>(configuration, Section)
            .ConfigurePrimaryHttpMessageHandler(() => RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITestClient>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("api/items"));

        Assert.Contains("BaseAddress", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidNestedConfiguration_FailsValidationOnFirstUse()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>()
        {
            [$"{Section}:BaseAddress"] = "https://config.test/",
            [$"{Section}:Resilience:CircuitBreaker:MinimumThroughput"] = "1",
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddResilientHttpClient<ITestClient, TestClient>(configuration, Section)
            .ConfigurePrimaryHttpMessageHandler(() => RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITestClient>();

        await Assert.ThrowsAsync<OptionsValidationException>(() => client.GetAsync("api/items"));
    }

    [Fact]
    public void PipelineCache_IsRegisteredAsSingleton()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddResilientHttpClient("first", configuration, "HttpClients:First")
            .ConfigurePrimaryHttpMessageHandler(() => RecordingHttpMessageHandler.Status(HttpStatusCode.OK));
        services.AddResilientHttpClient("second", configuration, "HttpClients:Second")
            .ConfigurePrimaryHttpMessageHandler(() => RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<ResiliencePipelineCache>(),
            provider.GetRequiredService<ResiliencePipelineCache>());
    }

    [Fact]
    public async Task NamedClient_ConfigureAction_IsApplied()
    {
        var inner = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddResilientHttpClient(
                "configured",
                configuration,
                "HttpClients:Configured",
                options => options.BaseAddress = "https://default.test/")
            .ConfigurePrimaryHttpMessageHandler(() => inner);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("configured");

        using var response = await client.GetAsync("api/items");

        Assert.Equal("https://default.test/api/items", inner.Requests[0].RequestUri!.ToString());
    }

    private static IConfigurationRoot BuildConfiguration(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Условие не выполнилось за {timeout}.");
    }
}
