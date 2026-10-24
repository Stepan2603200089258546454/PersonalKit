using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Common.Helper.Http.Tests.Infrastructure;

/// <summary>
/// Конфигурация в памяти, которую можно перезагрузить: используется для проверки горячей перезагрузки.
/// </summary>
internal sealed class ReloadableConfiguration
{
    private readonly MutableConfigurationSource _source;

    public ReloadableConfiguration(IDictionary<string, string?> data)
    {
        _source = new MutableConfigurationSource(data);
        Root = new ConfigurationBuilder().Add(_source).Build();
    }

    public IConfigurationRoot Root { get; }

    public IConfiguration Configuration => Root;

    public void Reload(IDictionary<string, string?> data) => _source.Provider.ReplaceAll(data);
}
