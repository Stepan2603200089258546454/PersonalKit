# Common.Security.OpenIddict.Client

Клиентская библиотека для **настольных, мобильных и консольных** приложений, которые
входят через OpenID Connect-провайдер (DevHome.Identity или другой совместимый сервер).

Библиотека не зависит от ASP.NET Core: её можно подключать к WPF, MAUI, консольному
приложению и любому другому .NET-клиенту.

## Что входит

| Тип | Назначение |
|---|---|
| `OpenIddictNativeClientOptions` / `OpenIddictNativeClient` | вход в WPF, MAUI и других нативных приложениях через системный браузер |
| `OpenIddictDeviceCodeClientOptions` / `OpenIddictDeviceCodeClient` | device code flow для CLI |

Для веб-приложений и API используйте `Common.Security.OpenIddict.Client.Web`.

## Требования

- .NET 11;
- `IdentityModel.OidcClient` 6 (подключается транзитивно).

## Настольное и мобильное приложение

Приложение передаёт платформенный браузер, который умеет открыть системный браузер
и принять код на loopback-адресе.

```csharp
using Common.Security.OpenIddict.Client;

var client = new OpenIddictNativeClient(new OpenIddictNativeClientOptions
{
    Authority = "http://127.0.0.1:5201/",
    ClientId = "myapp-wpf",
    RedirectUri = "http://127.0.0.1:5181/callback",
    Scope = "openid profile email offline_access myapp.api",
    Browser = new SystemBrowser(), // реализация IdentityModel.OidcClient.IBrowser
    AllowHttp = true,
    AcceptAnyServerCertificate = true
});

if (await client.LoginAsync())
{
    Console.WriteLine(client.User?.Identity?.Name);
    Console.WriteLine(client.AccessToken);
}
```

`AllowHttp` и `AcceptAnyServerCertificate` допустимы только для локального сервера.
В интернете используйте HTTPS и не принимайте произвольные сертификаты.

`OpenIddictNativeClient` хранит профиль и токены в памяти. Для долговременного хранения
refresh-токена приложение может сохранить `RefreshToken` само.

## Командная строка (device code flow)

```csharp
using Common.Security.OpenIddict.Client;

var client = new OpenIddictDeviceCodeClient(new OpenIddictDeviceCodeClientOptions
{
    Authority = "http://127.0.0.1:5201/",
    ClientId = "myapp-cli",
    Scope = "openid profile email offline_access myapp.api"
});

var prompt = await client.RequestAsync();
Console.WriteLine($"Откройте {prompt.VerificationUri}");
Console.WriteLine($"Введите код {prompt.UserCode}");

while (true)
{
    await Task.Delay(TimeSpan.FromSeconds(prompt.Interval));

    var result = await client.PollAsync(prompt);
    switch (result.Status)
    {
        case DeviceCodePollStatus.Success:
            Console.WriteLine(result.Token!.AccessToken);
            return;

        case DeviceCodePollStatus.Pending:
            continue;

        case DeviceCodePollStatus.SlowDown:
            // Увеличьте интервал и продолжите.
            continue;

        case DeviceCodePollStatus.AccessDenied:
        case DeviceCodePollStatus.Expired:
        case DeviceCodePollStatus.Error:
            Console.Error.WriteLine(result.Error);
            return;
    }
}
```

Методы `RefreshAsync` и `GetUserInfoAsync` помогают продлить токен и получить профиль.

## Что важно знать

- Публичные клиенты (WPF, MAUI, CLI) работают без секрета, только с PKCE.
- `AllowHttp` нужен только для локальной сети и разработки.
- Токены хранятся в памяти; после перезапуска приложения нужен повторный вход или
  сохранённый refresh-токен.
- Для веб-приложения с cookie и для API используйте `Common.Security.OpenIddict.Client.Web`.

## Примеры в репозитории

- `DevHome/DevHome.Wpf` — Blazor Hybrid WPF;
- `DevHome/DevHome.Maui` — Blazor Hybrid MAUI;
- `DevHome/DevHome.Cli` — device code flow.
