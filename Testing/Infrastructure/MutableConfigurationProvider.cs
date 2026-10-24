using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Common.Helper.Http.Tests.Infrastructure;

/// <summary>
/// Провайдер конфигурации с возможностью заменить значения и вызвать reload,
/// как это происходит при изменении appsettings.json.
/// </summary>
internal sealed class MutableConfigurationProvider : ConfigurationProvider
{
    public MutableConfigurationProvider(IDictionary<string, string?> data)
    {
        Data = new Dictionary<string, string?>(data, StringComparer.OrdinalIgnoreCase);
    }

    public void ReplaceAll(IDictionary<string, string?> data)
    {
        Data = new Dictionary<string, string?>(data, StringComparer.OrdinalIgnoreCase);
        OnReload();
    }
}
