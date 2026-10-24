# Правила рабочей области PersonalKit

Область: `E:\Projects\Programs\Infrastructure\PersonalKit`. Решение: `PersonalKit.slnx` (формат `.slnx`), .NET 10.
Все пути ниже — относительно корня рабочей области.

## 1. Где агент хранит свои файлы

| Что создаёт агент | Куда кладёт |
|---|---|
| Документы: заметки, планы, отчёты, результаты исследований, схемы, выгрузки | `AgentsDocs/` |
| Скрипты и утилиты: PowerShell, Python, CMD, Bash, разовые проверки | `AgentsTools/` |
| Временные и сгенерированные файлы (логи, дампы, кеши) | `AgentsTools/temp/` или системный temp, с удалением после задачи |

- Документы — Markdown в `kebab-case.md` (`http-retry-plan.md`); скрипты — `<Глагол>-<Объект>.ps1` (`Sync-Solution.ps1`).
- Вложенные темы оформляются подкаталогами внутри `AgentsDocs/` и `AgentsTools/`.
- **Запрещено** размещать файлы агента в корне рабочей области и внутри каталогов проектов (`Common.Helper.Http/` и любых новых).
- `AgentsDocs/` и `AgentsTools/` не участвуют в сборке: код продукта в них не размещается, проекты там не создаются.
- Временный мусор не остаётся в рабочей области после завершения задачи.

## 2. Синхронизация с решением `PersonalKit.slnx`

Скрипт `AgentsTools/Sync-Solution.ps1` управляет только двумя областями:

- **файлы рядом с slnx** (в корне рабочей области) — попадают в папку решения «Элементы решения»;
- **каталоги агента** `AgentsDocs/` и `AgentsTools/` — папки решения повторяют их физическую структуру.

Каталоги проектов скрипт не синхронизирует: их структура в обозревателе решений поддерживается вручную (Visual Studio, `dotnet sln add`) и при синхронизации не меняется.

Отображение физического пути в виртуальный:

| Физический путь | Обозреватель решений |
|---|---|
| `AGENTS.md`, `Directory.Build.props`, `Directory.Packages.props` | папка `/Элементы решения/` |
| `AgentsDocs/README.md` | папка `/AgentsDocs/` → `README.md` |
| `AgentsDocs/guides/http.md` | папка `/AgentsDocs/guides/` (вложенная) → `http.md` |
| `Common.Helper.Http/Common.Helper.Http.csproj` | папка `/Common.Helper.Http/` → проект `Common.Helper.Http` (расстановка — вручную) |

Правила:

- новый проект добавляется в решение через `dotnet sln add <путь к проекту>` (команда сама создаст папку по физическому пути) или через Visual Studio; скрипт проект не добавит, но выведет предупреждение;
- файлы внутри каталогов проектов принадлежат проектам и скриптом не трогаются;
- `bin/`, `obj/`, `artifacts/`, `.vs/`, `node_modules/`, `temp/`, `TestResults/`, `packages/` в решение не попадают;
- пустые каталоги не создаются: то, что не содержит файлов, в решении не отображается;
- `PersonalKit.slnx` записывается в UTF-8 с BOM и не редактируется вручную: файл генерируется скриптом. Правки, сделанные в Visual Studio (проекты, их вложенные элементы, `<Configurations>`), скрипт сохраняет и пересобирает только управляемые области.

После любого добавления, переименования, перемещения или удаления файлов в корне, `AgentsDocs/` или `AgentsTools/`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Sync-Solution.ps1
```

Проверка синхронности (код 0 — всё совпадает, 1 — есть расхождения, 2 — ошибка):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Sync-Solution.ps1 -Verify
```

## 3. Проекты и сборка

- Целевой фреймворк — `net10.0`; в новом проекте включаются `Nullable` и `ImplicitUsings` (как в `Common.Helper.Http`).
- Новый проект создаётся в отдельном каталоге в корне рабочей области; имя каталога совпадает с именем проекта.
- После `dotnet new` проект добавляется в решение командой `dotnet sln add <путь к проекту>` (папка решения создастся по физическому пути), затем выполняется сборка.
- Версии NuGet-пакетов задаются централизованно в `Directory.Packages.props` (`ManagePackageVersionsCentrally`). В проектах указывается только `<PackageReference Include="Имя.Пакета" />` без `Version`; новый пакет добавляется как `<PackageVersion Include="Имя.Пакета" Version="x.y.z" />` в `Directory.Packages.props`.
- Сборка решения:

  ```powershell
  dotnet build PersonalKit.slnx -m:1
  ```

  Результаты складываются в `artifacts/` (настроено в `Directory.Build.props`, в решении не отображается).

## 4. Тесты

- Тестовый проект: `Testing/Common.Helper.Http.Tests` (xunit.v3 на Microsoft.Testing.Platform). В обозревателе решений он лежит в папке `Common/Testing`; физический каталог — `Testing/`, виртуальный — `Common/Testing` (проекты в решении расставляются вручную).
- Запуск в песочнице DSH (in-process runner):

  ```powershell
  dotnet run --project Testing
  ```

  `dotnet test` в песочнице не работает: VSTest не может запустить testhost (изоляция процессов).

- Запуск в обычном окружении и в Visual Studio:

  ```powershell
  dotnet test PersonalKit.slnx -m:1
  ```

- Новые тесты добавляются в `Testing/` рядом с существующими, на xunit.v3; покрытие библиотеки `Common.Helper.Http` обязательно поддерживать.

## 5. Кодировка, терминал, окружение

- Все файлы — UTF-8. PowerShell-команды начинаются с:
  ```powershell
  chcp 65001 > $null
  [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
  $OutputEncoding = [System.Text.Encoding]::UTF8
  ```
- Пути с кириллицей поддерживаются; не перекодировать и не экранировать их.
- В системе только Windows PowerShell 5.1 с политикой `AllSigned`, поэтому локальные скрипты запускаются с `-ExecutionPolicy Bypass`.
- В песочнице DSH запись вне рабочей области запрещена, поэтому `dotnet` не может создать `%USERPROFILE%\.dotnet` и `%USERPROFILE%\.nuget`:
  ```powershell
  $env:DOTNET_CLI_HOME = Join-Path $env:TEMP 'dsh-dotnet-home'
  $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
  ```
- Пакеты уже скачаны в общий кэш, но `DOTNET_CLI_HOME` перенаправляет путь кэша; чтобы сборка не лезла в сеть, задавайте кэш явно:
  ```powershell
  $env:NUGET_PACKAGES = Join-Path $env:USERPROFILE '.nuget\packages'
  ```
- Песочница запрещает именованные каналы, поэтому MSBuild не может запускать дочерние узлы: сборка решения в песочнице выполняется с `-m:1` (без флага сборка падает без сообщений об ошибках).

## 6. Чек-лист перед завершением задачи

- [ ] документы и скрипты агента лежат только в `AgentsDocs/` и `AgentsTools/`;
- [ ] `Sync-Solution.ps1 -Verify` вернул код 0;
- [ ] версии NuGet-пакетов заданы только в `Directory.Packages.props`;
- [ ] `dotnet build PersonalKit.slnx -m:1` проходит без ошибок;
- [ ] `dotnet run --project Testing` завершился успешно;
- [ ] в рабочей области не осталось временных файлов.
