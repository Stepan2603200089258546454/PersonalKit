# AgentsTools

Каталог скриптов и утилит агента: синхронизация решения, проверки, разовые операции.
Каталог не участвует в сборке.

## Sync-Solution.ps1

Синхронизирует `PersonalKit.slnx` с файловой системой, но управляет только двумя областями:

- **файлы рядом с файлом решения** (в корне рабочей области) — складываются в папку решения
  «Элементы решения»;
- **каталоги `AgentsDocs/` и `AgentsTools/`** — папки решения повторяют их физическую структуру.

Каталоги проектов не синхронизируются: их структура в обозревателе решений поддерживается
вручную (Visual Studio, `dotnet sln add`). Скрипт никогда не добавляет, не перемещает и не удаляет
проекты и их вложенные элементы — при перезаписи они сохраняются как есть (включая
`<Configurations>` и файлы, добавленные в проект вручную). Если проект есть на диске, но отсутствует
в решении, скрипт выводит предупреждение.

Решение записывается в UTF-8 с BOM.

```powershell
# записать изменения
powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Sync-Solution.ps1

# только проверить: 0 — синхронизировано, 1 — есть расхождения, 2 — ошибка
powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Sync-Solution.ps1 -Verify

# показать новое содержимое решения, не записывая файл
powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Sync-Solution.ps1 -DryRun

# указать другое решение
powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Sync-Solution.ps1 -SolutionPath path/to/Other.slnx
```

Во всех командах нужен `-ExecutionPolicy Bypass`: на этой машине политика `AllSigned` запрещает
запуск локальных неподписанных скриптов.

## Start-Docker.ps1

Собирает и запускает `DevHome`, `DevHome.Identity` и `DevHome.Api` в Docker Desktop.
Скрипт создаёт `.env` из `.env.example`, записывает версию, режим сборки и адрес
в локальной сети, при необходимости публикует приложения и вызывает
`docker compose up -d --build`.

```powershell
# сборка из исходников, версия 0.1.0
powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Start-Docker.ps1 -PublicHost 192.168.1.50

# готовое опубликованное приложение, версия 0.2.0
powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Start-Docker.ps1 -Version 0.2.0 -Mode published -PublicHost 192.168.1.50

# только запустить уже собранные образы
powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Start-Docker.ps1 -NoBuild

# интерактивное меню (его открывает Build-Docker.cmd двойным кликом)
powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Start-Docker.ps1 -Interactive
```

Параметры и порядок работы описаны в `AgentsDocs/docker.md`.

### Что попадает в решение

- файлы из корня рабочей области (кроме самого решения) — в папку `/Элементы решения/`;
- файлы из `AgentsDocs/` и `AgentsTools/` — в папки, совпадающие с их каталогами;
- вложенные каталоги агента — во вложенные папки решения.

### Что не попадает и не меняется

- проекты и каталоги проектов (управляются вручную);
- `bin/`, `obj/`, `artifacts/`, `publish/`, `.vs/`, `.git/`, `node_modules/`, `temp/`, `TestResults/`, `packages/`;
- служебные файлы (`*.user`, `*.suo`, `*.tmp`, `*.log`, `*.binlog`), локальные секреты (`.env`), временные файлы Office (`~$*`);
- сам файл решения.

### Коды возврата

| Код | Значение |
|---|---|
| 0 | решение синхронизировано (или записано без ошибок) |
| 1 | `-Verify`: в решении есть расхождения |
| 2 | ошибка выполнения (нет решения, не удалось прочитать/записать файл) |
