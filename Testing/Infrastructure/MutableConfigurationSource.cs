using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Common.Helper.Http.Tests.Infrastructure;

/// <summary>Источник конфигурации, отдающий <see cref="MutableConfigurationProvider"/>.</summary>
internal sealed class MutableConfigurationSource : IConfigurationSource
{
    public MutableConfigurationSource(IDictionary<string, string?> data)
    {
        Provider = new MutableConfigurationProvider(data);
    }

    public MutableConfigurationProvider Provider { get; }

    public IConfigurationProvider Build(IConfigurationBuilder builder) => Provider;
}
