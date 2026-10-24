using System;
using System.Net.Http;
using System.Threading.Tasks;
using Common.Helper.Http.Tests.Infrastructure;
using Polly;

namespace Common.Helper.Http.Tests;

public sealed class ResiliencePipelineCacheTests
{
    [Fact]
    public void SameOptions_ReturnsSamePipeline()
    {
        var cache = new ResiliencePipelineCache();
        var options = TestOptions.Fast();

        var first = cache.GetPipeline("client", options);
        var second = cache.GetPipeline("client", options);

        Assert.Same(first, second);
    }

    [Fact]
    public void NewOptionsInstance_RebuildsPipeline()
    {
        var cache = new ResiliencePipelineCache();

        var first = cache.GetPipeline("client", TestOptions.Fast());
        var second = cache.GetPipeline("client", TestOptions.Fast());

        Assert.NotSame(first, second);
    }

    [Fact]
    public void DifferentRetryModes_ReturnDifferentPipelines()
    {
        var cache = new ResiliencePipelineCache();
        var options = TestOptions.Fast();

        var withRetry = cache.GetPipeline("client", options, allowRetry: true);
        var withoutRetry = cache.GetPipeline("client", options, allowRetry: false);

        Assert.NotSame(withRetry, withoutRetry);
        Assert.Same(withRetry, cache.GetPipeline("client", options, allowRetry: true));
        Assert.Same(withoutRetry, cache.GetPipeline("client", options, allowRetry: false));
    }

    [Fact]
    public void DifferentClients_AreIsolated()
    {
        var cache = new ResiliencePipelineCache();
        var options = TestOptions.Fast();

        var first = cache.GetPipeline("client-a", options);
        var second = cache.GetPipeline("client-b", options);

        Assert.NotSame(first, second);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void EmptyClientName_Throws(string? clientName)
    {
        var cache = new ResiliencePipelineCache();

        Assert.ThrowsAny<ArgumentException>(() => cache.GetPipeline(clientName!, TestOptions.Fast()));
    }

    [Fact]
    public void NullOptions_Throws()
    {
        var cache = new ResiliencePipelineCache();

        Assert.Throws<ArgumentNullException>(() => cache.GetPipeline("client", null!));
    }

    [Fact]
    public void ConcurrentCalls_ReturnSamePipeline()
    {
        var cache = new ResiliencePipelineCache();
        var options = TestOptions.Fast();
        var pipelines = new ResiliencePipeline<HttpResponseMessage>[64];

        Parallel.For(0, pipelines.Length, index => pipelines[index] = cache.GetPipeline("client", options));

        Assert.All(pipelines, pipeline => Assert.Same(pipelines[0], pipeline));
    }

    [Fact]
    public async Task ConcurrentRequests_SharePipeline()
    {
        var cache = new ResiliencePipelineCache();
        var options = TestOptions.Fast();
        var inner = RecordingHttpMessageHandler.Status(System.Net.HttpStatusCode.OK);

        using var harness = new ResilientHandlerHarness(options, inner, cache: cache);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => harness.SendAsync("api/items")));

        Assert.All(responses, response => Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(20, inner.CallCount);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public void NoRetryMode_DoesNotRetry()
    {
        var cache = new ResiliencePipelineCache();
        var pipeline = cache.GetPipeline("client", TestOptions.Fast(), allowRetry: false);
        var calls = 0;

        using var response = pipeline.Execute(() =>
        {
            calls++;
            return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
        });

        Assert.Equal(1, calls);
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public void NoRetryEntry_IsRebuiltForNewOptions()
    {
        var cache = new ResiliencePipelineCache();
        var firstOptions = TestOptions.Fast();
        var secondOptions = TestOptions.Fast();

        var first = cache.GetPipeline("client", firstOptions, allowRetry: false);
        var second = cache.GetPipeline("client", secondOptions, allowRetry: false);
        var repeated = cache.GetPipeline("client", secondOptions, allowRetry: false);

        Assert.NotSame(first, second);
        Assert.Same(second, repeated);
    }

    [Fact]
    public void RetryAndNoRetryModes_AreReloadedIndependently()
    {
        var cache = new ResiliencePipelineCache();
        var options = TestOptions.Fast();

        var withRetry = cache.GetPipeline("client", options, allowRetry: true);
        var withoutRetry = cache.GetPipeline("client", options, allowRetry: false);

        var reloaded = TestOptions.Fast();
        var withRetryAfterReload = cache.GetPipeline("client", reloaded, allowRetry: true);
        var withoutRetryAfterReload = cache.GetPipeline("client", reloaded, allowRetry: false);

        Assert.NotSame(withRetry, withRetryAfterReload);
        Assert.NotSame(withoutRetry, withoutRetryAfterReload);
        Assert.Same(withRetryAfterReload, cache.GetPipeline("client", reloaded, allowRetry: true));
        Assert.Same(withoutRetryAfterReload, cache.GetPipeline("client", reloaded, allowRetry: false));
    }
}
