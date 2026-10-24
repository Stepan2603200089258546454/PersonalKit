# Типизированный HTTP-клиент

Основное руководство — в корне библиотеки: [`Common.Helper.Http/README.md`](../Common.Helper.Http/README.md).
Примеры отдельно для сервера и отдельно для Blazor WASM — в
[`Common.Helper.Http/usage-server-and-wasm.md`](../Common.Helper.Http/usage-server-and-wasm.md).
Там регистрация в DI, полная схема `appsettings.json`, горячая перезагрузка, поведение
таймаутов/повторов/предохранителя и ограничения.

Кратко:

- регистрация: `services.AddResilientHttpClient<TClient, TImplementation>(configuration, "HttpClients:Имя")`;
- fluent-запросы: `httpClient.NewRequest("api/items").WithJsonContent(value).SendJsonAsync<T>()`,
  а также формы, multipart, потоки, байты и текст;
- параметры — секция `HttpClients:Имя` в `appsettings.json`; для перезагрузки нужен провайдер
  конфигурации с `reloadOnChange: true`;
- относительные адреса разрешены: `BaseAddress` подставляется обработчиком перед каждым запросом;
- тесты: `dotnet run --project Testing` (в песочнице DSH),
  `dotnet test PersonalKit.slnx -m:1` (обычное окружение, Visual Studio).
