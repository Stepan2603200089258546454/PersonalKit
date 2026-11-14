# DevHome и DevHome.Identity в Docker Desktop

Документ описывает локальный запуск приложений в Docker Desktop, чтобы ими можно было
пользоваться с других устройств домашней сети. База данных остаётся в существующем
контейнере PostgreSQL; в Docker запускаются только приложения.

Для первого запуска есть короткая инструкция: [docker-quickstart.md](docker-quickstart.md).

## 1. Что запускается

| Контейнер | Что это | Адрес на хосте | Доступ из локальной сети |
|---|---|---|---|
| `identity` | `DevHome.Identity`: вход, пользователи, OpenIddict, админка | `http://<IP>:5201/` | да |
| `web` | `DevHome`: веб-приложение | `http://<IP>:5149/` | да |
| `api` | `DevHome.Api`: шаблон API | нет published-порта | только внутри Docker |
| `postgres_db` | существующая PostgreSQL | `localhost:5432` | не меняется |

Схема связей:

```text
Браузер в локальной сети
        │
        ├── http://192.168.1.50:5201 ──► identity:8080 ──► postgres_db:5432
        │                                      ▲
        └── http://192.168.1.50:5149 ──► web:8080 ──┘ (серверный обмен OIDC)
                                               │
                                          api:8080 (только сеть devhome)
```

`web` и `api` обращаются к Identity по публичному адресу `PUBLIC_HOST:5201`.
Внутри контейнера этот адрес разрешается через `host-gateway`, поэтому вход работает,
даже если Docker не умеет «ходить» на собственный внешний IP.

## 2. Требования

- Windows 10/11 с Docker Desktop (Linux-контейнеры, WSL 2).
- Запущенный Docker Desktop.
- Контейнер PostgreSQL. В этом проекте предполагается `postgres_db` в сети
  `postgresql_backend` и база `DevHome.Identity`.
- Для режима `source` .NET SDK на хосте не нужен: всё собирается в контейнере.
- Для режима `published` нужен .NET SDK 11 на хосте: приложения публикуются до сборки образа.
- Свободные порты `5201` и `5149` (или другие, заданные в `.env`).

## 3. Файлы

| Файл | Назначение |
|---|---|
| `docker-compose.yml` | описание трёх приложений, сетей и томов |
| `Build-Docker.cmd` | меню сборки/публикации для двойного клика |
| `.env.example` | шаблон настроек; копируется в `.env` |
| `.env` | локальные значения и секреты, в git не попадает |
| `DevHome.Identity/DevHome.Identity/Dockerfile` | образ Identity, режимы `source` и `published` |
| `DevHome/DevHome/Dockerfile` | образ DevHome |
| `DevHome/DevHome.Api/Dockerfile` | образ API |
| `.dockerignore` | что не передавать в контекст сборки |
| `AgentsTools/Start-Docker.ps1` | подготовка `.env`, публикация и `docker compose up` |
| `*/appsettings.Production.json` | настройки Docker, примонтированы в контейнеры |
| `DevHome/DevHome.Api/` | исходники шаблонного API |

`DevHome.Client` — это WebAssembly-часть DevHome, а не отдельный сервер. Она собирается
внутри образа `web`; отдельного контейнера для неё нет.

## 4. Первый запуск

### 4.1. Узнать адрес в локальной сети

```powershell
ipconfig
```

Нужен **IPv4-адрес** активного адаптера, например `192.168.1.50`. Не подходят:

- `127.0.0.1` и `localhost` — это только сам компьютер, другие устройства их не видят;
- `169.254.x.x` — служебный адрес;
- `172.17.x.x`–`172.31.x.x` — обычно внутренние сети Docker/WSL;
- адреса `vEthernet` — виртуальные адаптеры Hyper-V/WSL.

Если адрес компьютера меняется (DHCP), лучше закрепить его в роутере или задать
статический адрес. При смене адреса нужно обновить `.env` (см. раздел 8).

### 4.2. Запустить одной командой

Из корня решения:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Start-Docker.ps1 -Version 0.1.0 -PublicHost 192.168.1.50
```

Скрипт:

1. проверяет Docker Desktop;
2. создаёт `.env` из `.env.example`, если файла ещё нет;
3. записывает `PUBLIC_HOST`, `APP_VERSION`, `BUILD_MODE`;
4. в режиме `published` публикует приложения в `publish/`;
5. выполняет `docker compose up -d --build`;
6. печатает адреса и команды диагностики.

Если `-PublicHost` не указать, скрипт попробует определить адрес сам. Автоматическое
определение может ошибиться, поэтому лучше указывать адрес явно.

### 4.2.1. Запуск двойным кликом

Рядом с `docker-compose.yml` лежит `Build-Docker.cmd`. Двойной клик открывает окно,
в котором нужно выбрать:

- `1` — собрать из исходников (`source`);
- `2` — опубликовать приложения в `publish/` и собрать из готовых файлов (`published`);
- `0` — выйти.

Затем можно подтвердить версию и адрес (просто нажмите Enter, чтобы оставить текущие).
Окно не закрывается само: в конце будет видно результат и адреса. Этот файл —
тонкая обёртка, вся логика по-прежнему в `AgentsTools/Start-Docker.ps1`.

### 4.3. Что происходит при первом старте

1. `identity` подключается к `postgres_db` и ждёт базу до 60 секунд.
2. Применяются миграции EF Core.
3. Создаются области и клиенты OpenIddict (`devhome-web`, `devhome-service`, `devhome-api`).
4. Создаются роли `User` и `Admin`.
5. Назначается администратор из `ADMIN_EMAIL`.
6. `/health` начинает отвечать `200`; только после этого запускаются `web` и `api`.

В журнале первого запуска могут быть сообщения EF о том, что таблицы
`__EFMigrationsHistory` ещё нет: это нормально, миграция создаёт её следом.

Если база пустая, администратор создаётся с паролем `ADMIN_PASSWORD` из `.env`.
Если пользователь с такой почтой уже есть, его пароль **не меняется** — используется
существующий. Сразу после первого входа смените пароль в личном кабинете.

### 4.4. Открыть приложения

- Identity: `http://192.168.1.50:5201/`
- DevHome: `http://192.168.1.50:5149/`
- Админка: `http://192.168.1.50:5201/admin`
- Метаданные OIDC: `http://192.168.1.50:5201/.well-known/openid-configuration`

Кнопка «Войти» в DevHome перенаправляет на Identity и возвращает обратно после входа.
Для входа нужна учётная запись с подтверждённой почтой.

## 5. Настройки `.env`

`.env` создаётся из `.env.example`. Основные значения:

| Переменная | По умолчанию | Что означает |
|---|---|---|
| `PUBLIC_HOST` | `192.168.1.100` в шаблоне | IPv4-адрес или имя компьютера в локальной сети, без `http://` и порта |
| `APP_VERSION` | `0.1.0` | версия сборки: тег образа, `/health`, номер сборки |
| `BUILD_MODE` | `source` | `source` — сборка из исходников, `published` — из `publish/` |
| `IDENTITY_PORT` | `5201` | порт Identity на хосте |
| `WEB_PORT` | `5149` | порт DevHome на хосте |
| `ADMIN_EMAIL` | почта владельца | пользователь, которому выдаётся роль `Admin` |
| `ADMIN_PASSWORD` | значение из `.env` | пароль для создания администратора в пустой базе |
| `POSTGRES_NETWORK` | `postgresql_backend` | сеть существующего контейнера PostgreSQL |
| `POSTGRES_HOST` | `postgres_db` | имя контейнера PostgreSQL в этой сети |
| `POSTGRES_DB` | `DevHome.Identity` | имя базы |
| `POSTGRES_USER` | `postgres` | пользователь базы |
| `POSTGRES_PASSWORD` | `SuperSecretPassword123!` | пароль базы |
| `POSTGRES_PORT` | `5432` | порт базы внутри сети Docker |
| `WEB_CLIENT_SECRET` | `devhome-web-development-secret` | секрет клиента `devhome-web` |
| `SERVICE_CLIENT_SECRET` | `devhome-service-development-secret` | секрет сервисного клиента |
| `API_CLIENT_SECRET` | `devhome-api-development-secret` | секрет клиента проверки токенов |
| `OPENIDDICT_CERT_PASSWORD` | `devhome-docker-certificate` | пароль PFX-сертификатов OpenIddict |

`.env` содержит пароли и в git не добавляется (правило есть в `.gitignore`).
Для домашней сети значения по умолчанию допустимы, но пароль администратора
и секреты лучше заменить.

### 5.1. Что перенесено в `appsettings.Production.json`

Часть настроек больше не задаётся в `.env`. Она относится к работе в Docker
и лежит в файлах приложений:

| Файл | Что задаёт |
|---|---|
| `DevHome.Identity/DevHome.Identity/appsettings.Production.json` | HTTP без редиректа, `OpenIddict:AllowHttp`, обновление клиентов, каталог сертификатов, версия PostgreSQL и размер пула |
| `DevHome/DevHome/appsettings.Production.json` | HTTP без редиректа, разрешение HTTP для OIDC |
| `DevHome/DevHome.Api/appsettings.Production.json` | HTTP без редиректа, `ClientId` для introspection |

В `docker-compose.yml` эти три файла примонтированы в контейнеры:

```text
/app/appsettings.Production.json
```

Поэтому их можно править на хосте и применять изменения командой:

```powershell
docker compose restart identity web api
```

Пересобирать образ после правки `appsettings.Production.json` не нужно.
Файл `appsettings.json` (без `.Production`) встроен в образ: его изменения
требуют пересборки.

Переменные окружения из `.env` по-прежнему имеют приоритет над обоими файлами.
В `.env` остаются секреты и то, что зависит от компьютера: адрес в сети, порты,
строка подключения к БД, пароли клиентов, issuer, адрес возврата.

## 6. Два режима сборки

### 6.1. `source` — из исходников

Docker копирует весь репозиторий (кроме `.dockerignore`), восстанавливает NuGet-пакеты
и выполняет `dotnet publish` внутри SDK-образа. Все проектные ссылки и общие библиотеки
попадают в образ автоматически, вручную перечислять их не нужно.

```powershell
$env:BUILD_MODE = 'source'
$env:APP_VERSION = '0.1.0'
docker compose up -d --build
```

Плюсы: на хосте не нужен .NET SDK, воспроизводимая сборка.
Минусы: дольше (скачивание пакетов и сборка внутри контейнера).

### 6.2. `published` — из готового приложения

Сначала приложения публикуются на хосте:

```powershell
dotnet publish DevHome.Identity/DevHome.Identity/DevHome.Identity.csproj -c Release -o publish/Identity -p:Version=0.1.0 -p:InformationalVersion=0.1.0 -p:IncludeSourceRevisionInInformationalVersion=false
dotnet publish DevHome/DevHome/DevHome.csproj -c Release -o publish/Web -p:Version=0.1.0 -p:InformationalVersion=0.1.0 -p:IncludeSourceRevisionInInformationalVersion=false
dotnet publish DevHome/DevHome.Api/DevHome.Api.csproj -c Release -o publish/Api -p:Version=0.1.0 -p:InformationalVersion=0.1.0 -p:IncludeSourceRevisionInInformationalVersion=false
```

Затем Docker только копирует готовые файлы:

```powershell
$env:BUILD_MODE = 'published'
$env:APP_VERSION = '0.1.0'
docker compose up -d --build
```

Или одной командой, которая сделает и публикацию, и сборку:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Start-Docker.ps1 -Version 0.2.0 -Mode published -PublicHost 192.168.1.50
```

Плюсы: быстро, можно собрать на машине с SDK и запустить на другой.
Минусы: нужен .NET SDK 11 и каталог `publish/`; старые файлы в `publish/` могут
попасть в образ, поэтому скрипт очищает каталог перед публикацией.

### 6.3. Как это устроено в Dockerfile

В каждом Dockerfile есть стадии:

- `runtime` — общая среда выполнения, `curl`, `HEALTHCHECK`, пользователь `app`;
- `source-build` — SDK, `dotnet publish`;
- `published` — копирование `publish/<App>`;
- `source` — копирование результата `source-build`.

`docker compose` выбирает стадию через `target: ${BUILD_MODE}`. Если собирать Dockerfile
напрямую без `--target`, по умолчанию используется последняя стадия `source`:

```powershell
docker build -f DevHome.Identity/DevHome.Identity/Dockerfile -t devhome/identity:0.1.0 .
```

## 7. Версия

Версия задаётся `APP_VERSION` и передаётся в сборку как `Version` и
`InformationalVersion`. Она попадает:

- в тег образа: `devhome/identity:0.1.0`, `devhome/web:0.1.0`, `devhome/api:0.1.0`;
- в ответ `GET /health` каждого приложения;
- в метку OCI-образа `org.opencontainers.image.version`.

Проверить версию:

```powershell
Invoke-RestMethod http://192.168.1.50:5201/health
Invoke-RestMethod http://192.168.1.50:5149/health
docker compose exec api curl -fsS http://localhost:8080/health
```

Выпустить новую версию:

1. Измените `APP_VERSION` в `.env` (например, на `0.2.0`).
2. Выполните:

   ```powershell
   docker compose up -d --build
   ```

   или:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Start-Docker.ps1 -Version 0.2.0 -PublicHost 192.168.1.50
   ```

Старые образы остаются на диске. Удалить неиспользуемые:

```powershell
docker image prune
```

## 8. Порты, локальная сеть и смена адреса

Порты публикуются на все интерфейсы компьютера, поэтому доступны другим устройствам сети.
Windows может запросить разрешение брандмауэра при первом запуске — разрешите
входящие подключения для Docker Desktop в частной сети.

Если `PUBLIC_HOST` изменился (например, компьютер получил другой IP):

1. обновите `PUBLIC_HOST` в `.env`;
2. выполните `docker compose up -d` (образы пересобирать не обязательно);
3. Identity при старте обновит адрес возврата клиента `devhome-web`
   (включено `OpenIddict:Seed:RefreshClients=true`);
4. откройте приложения по новому адресу; старые cookie относятся к старому адресу,
   поэтому вход может потребоваться заново.

Если порт занят (например, запущено приложение из Visual Studio):

- закройте отладочный запуск; или
- поменяйте `IDENTITY_PORT`/`WEB_PORT` в `.env` и перезапустите.

### 8.1. Проверка с другого устройства

1. Подключите телефон или другой компьютер к **той же** домашней сети, что и ПК.
2. Откройте в браузере:

   ```text
   http://192.168.1.50:5201/health
   ```

   Должен вернуться JSON вида `{"status":"ok","database":"ready","version":"0.1.0"}`.
   Затем откройте `http://192.168.1.50:5149/`.

3. Если не открывается, проверьте по порядку:

   - адрес: именно IPv4 из `ipconfig`, без `localhost`;
   - сеть: телефон не в «гостевой» сети и не в гостевом Wi-Fi;
   - роутер: отключено ли «изолировать клиентов» / «AP isolation» /
     «Client isolation» (частая настройка гостевых сетей);
   - брандмауэр Windows: правило `Docker Desktop Backend` включено;
   - VPN на ПК: некоторые VPN меняют маршруты и блокируют входящие;
   - IP не изменился после перезагрузки роутера.

Проверить порты и правило на ПК:

```powershell
Get-NetTCPConnection -State Listen -LocalPort 5201,5149 |
    Select-Object LocalAddress,LocalPort,OwningProcess

Get-NetFirewallRule -DisplayName 'Docker Desktop Backend' |
    Select-Object DisplayName,Enabled,Profile,Action
```

Docker Desktop при первом запуске создаёт разрешающее правило для
`com.docker.backend.exe` на профили Private и Public. Если правило было
отключено или удалено, его можно создать заново (от имени администратора):

```powershell
New-NetFirewallRule `
    -DisplayName 'Docker Desktop Backend (DevHome)' `
    -Direction Inbound `
    -Program "$env:ProgramFiles\Docker\Docker\resources\com.docker.backend.exe" `
    -Action Allow `
    -Profile Private,Public
```

Если IP компьютера выдаётся по DHCP и может измениться, закрепите адрес
в роутере (резервирование по MAC-адресу). После смены IP обновите
`PUBLIC_HOST` в `.env` и выполните `docker compose up -d`.

## 9. База данных

Compose **не создаёт** PostgreSQL. Он подключается к уже существующему контейнеру
по внешней сети:

```yaml
networks:
  postgresql_backend:
    external: true
```

`identity` состоит в двух сетях: `devhome` (для общения с `web`/`api`) и
`postgresql_backend` (для доступа к базе).

Если при запуске ошибка `network postgresql_backend not found`:

```powershell
docker network create postgresql_backend
docker network connect postgresql_backend postgres_db
```

Если контейнер базы называется иначе, задайте `POSTGRES_NETWORK` и `POSTGRES_HOST`.
Проверить сеть:

```powershell
docker inspect postgres_db --format '{{json .NetworkSettings.Networks}}'
```

### 9.1. Миграции

Identity применяет миграции EF Core автоматически при старте
(`Database:AutoMigrate=true`). Если база ещё не готова, делается до 12 попыток
с интервалом 5 секунд. Пока база не готова, `/health` отвечает `503`, и `web`/`api`
не запускаются.

Отключить автоматические миграции (например, применять вручную):

```yaml
environment:
  Database__AutoMigrate: "false"
```

### 9.2. Данные и резервные копии

Пользователи, клиенты, токены и области хранятся в PostgreSQL. Пересоздание
контейнеров приложений (`docker compose down`, `up`) данные не удаляет.
`docker compose down -v` удаляет только тома приложений (`identity-data`, `web-data`),
но **не** базу.

Резервная копия:

```powershell
docker exec postgres_db pg_dump -U postgres -d DevHome.Identity -Fc -f /tmp/devhome.dump
docker cp postgres_db:/tmp/devhome.dump .\devhome.dump
```

Восстановление (осторожно, перезаписывает объекты базы):

```powershell
docker cp .\devhome.dump postgres_db:/tmp/devhome.dump
docker exec postgres_db pg_restore -U postgres -d DevHome.Identity --clean --if-exists /tmp/devhome.dump
```

### 9.3. Пароль базы

`POSTGRES_PASSWORD` в `.env` должен совпадать с паролем контейнера `postgres_db`.
Если меняете пароль в PostgreSQL, поменяйте и в `.env`, затем:

```powershell
docker compose up -d
```

Специальные символы `;`, `"` и `$` в пароле усложняют строку подключения. Для домашней
сети проще использовать пароль без этих символов.

## 10. Identity, OpenIddict и учётные записи

### 10.1. Issuer и адрес возврата

`OpenIddict:Issuer` в compose равен `http://<PUBLIC_HOST>:5201/`. Этот адрес:

- попадает в токены как `iss`;
- используется в метаданных OIDC;
- должен быть доступен браузеру.

Клиент `devhome-web` получает адрес возврата
`http://<PUBLIC_HOST>:5149/signin-oidc`. При старте Identity обновляет его,
поэтому смена IP не требует ручной правки клиента.

В Docker включено `OpenIddict:Seed:RefreshClients=true`: адреса возврата и секреты
клиентов `devhome-web`, `devhome-service`, `devhome-api` и ресурсы области
`devhome.api` приводятся к значениям из `.env` при каждом старте. Если вы
отредактировали этих клиентов в админке, после перезапуска Identity правки будут
перезаписаны. Для остальных клиентов (MAUI, WPF, CLI) обновление не выполняется.

### 10.2. Администратор

Роль `Admin` назначается пользователю `ADMIN_EMAIL` при старте Identity.
Проверка выполняется один раз при запуске: если вы зарегистрировали нового
пользователя и хотите сделать его администратором, перезапустите Identity:

```powershell
docker compose restart identity
```

В пустой базе администратор создаётся сразу, с подтверждённой почтой.
Пароль берётся из `ADMIN_PASSWORD`. После создания пользователя значение
`ADMIN_PASSWORD` можно очистить: существующий пароль оно не меняет.

Демонстрационный пользователь `demo@devhome.local` создаётся только в Development.
В контейнере он не появляется; в существующей базе он может остаться с прошлых
локальных запусков.

### 10.3. Регистрация и подтверждение почты

SMTP в проекте не настроен. Поэтому:

- новый пользователь может зарегистрироваться, но почта останется неподтверждённой;
- администратор подтверждает почту в `/admin/users/{id}`;
- проще создавать пользователей сразу в админке: `/admin/users/new`,
  с включённым флажком подтверждения почты.

### 10.4. Ключи и сертификаты

В томе `identity-data` лежат:

- ключи Data Protection (`/app/App_Data/DataProtection-Keys`);
- самоподписанные сертификаты OpenIddict (`/app/App_Data/OpenIddict/signing.pfx`
  и `encryption.pfx`).

Сертификаты создаются при первом запуске и переживают перезапуск контейнера.
Это важно: при их удалении все ранее выданные токены станут недействительными,
и пользователям придётся войти заново.

Удалить том (и все ключи) можно командой:

```powershell
docker compose down -v
```

### 10.5. HTTP, а не HTTPS

В локальной сети приложения работают по HTTP: сертификат на IP-адрес без домена
браузер всё равно не примет. Эти настройки лежат в `appsettings.Production.json`:

```json
{
  "Http": { "UseHttpsRedirection": false }
}
```

Для Identity дополнительно `OpenIddict:AllowHttp: true`, для DevHome —
`Authentication:DevHome:AllowHttp: true`. Это допустимо только в доверенной
домашней сети. Не открывайте порты `5201`/`5149` в интернет и не делайте проброс
на роутере. Для внешнего доступа нужен reverse proxy с HTTPS и постоянным доменом.

### 10.6. Правка `appsettings` в контейнере

Файлы `appsettings.Production.json` примонтированы из репозитория, поэтому
основной способ — править их на хосте:

```powershell
# откройте файл в любом редакторе, сохраните
notepad DevHome.Identity\DevHome.Identity\appsettings.Production.json

# примените изменения без пересборки
docker compose restart identity
```

Для DevHome и API — соответствующие файлы и сервисы `web`/`api`.

Попасть внутрь контейнера и посмотреть файл:

```powershell
docker compose exec identity sh
cat /app/appsettings.Production.json
cat /app/appsettings.json
exit
```

В контейнере нет `nano`/`vi`, а приложение работает под пользователем `app`.
Поэтому редактировать файл изнутри неудобно: для просмотра используйте `cat`,
а для изменений — файл на хосте (он примонтирован) или `docker compose cp`.
Если всё же нужно писать изнутри, запускайте `sh` от root и перезаписывайте
**весь** JSON целиком, иначе легко потерять остальные настройки:

```powershell
docker compose exec -u root identity sh
cat /app/appsettings.Production.json   # сначала посмотреть текущее содержимое
# затем cat > /app/appsettings.Production.json и вставить полный JSON
exit
docker compose restart identity
```

`appsettings.json` (без `.Production`) не примонтирован и находится внутри образа.
Его изменения нужно вносить в репозиторий и пересобирать:

```powershell
docker compose up -d --build identity
```

Быстрый разовый вариант без пересборки — скопировать файл в контейнер:

```powershell
docker compose cp .\DevHome.Identity\DevHome.Identity\appsettings.json identity:/app/appsettings.json
docker compose restart identity
```

Такая копия исчезнет при пересоздании контейнера (`docker compose up --force-recreate`).
Для постоянного изменения правьте файл в репозитории.

Порядок приоритета настроек ASP.NET Core:

1. `appsettings.json`;
2. `appsettings.Production.json`;
3. переменные окружения из `docker-compose.yml` / `.env`;
4. аргументы командной строки.

Если значение осталось в `.env`, оно перекроет то же значение в `appsettings`.
Поэтому после переноса настроек соответствующие переменные из compose удалены.

## 11. Шаблонное API

`DevHome.Api` — отдельное приложение, которое не публикует порт на хост. Оно доступно
только контейнерам сети `devhome`. Подробнее об эндпоинтах — [api-template.md](api-template.md).

Проверка публичного метода изнутри контейнера:

```powershell
docker compose exec -T api curl -fsS http://localhost:8080/api/public/ping
```

Получение сервисного токена и вызов приватного метода:

```powershell
$json = docker compose exec -T api curl -fsS -X POST http://identity:8080/connect/token -d grant_type=client_credentials -d client_id=devhome-service -d client_secret=devhome-service-development-secret -d scope=devhome.api
$token = ($json | ConvertFrom-Json).access_token
docker compose exec -T api curl -fsS -H "Authorization: Bearer $token" http://localhost:8080/api/private/me
```

Ответ должен содержать `"message":"Токен действителен."`. Без токена API возвращает
`401`; метод `/api/private/admin` для сервисного токена возвращает `403`, потому что
у сервиса нет роли `Admin`.

API проверяет токен через introspection: отправляет его в
`http://identity:8080/connect/introspect` от имени клиента `devhome-api`.
Клиент создаётся сидером Identity. В аудиторию токена добавлен `devhome-api`,
иначе OpenIddict запрещает introspection чужого токена.

## 12. Ежедневные команды

```powershell
# состояние
docker compose ps

# журналы (Ctrl+C — выйти)
docker compose logs -f identity web api

# перезапустить одно приложение
docker compose restart web

# пересобрать и обновить всё
docker compose up -d --build

# остановить, данные и ключи остаются
docker compose down

# остановить и удалить ключи приложений (база не тронется)
docker compose down -v

# образы и использование диска
docker images
docker system df
```

Обновление версии:

1. Сделайте резервную копию базы (раздел 9.2).
2. Выполните скрипт с новой версией:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Start-Docker.ps1 -Version 0.2.0 -PublicHost 192.168.1.50
   ```

3. Проверьте `/health` и вход.

## 13. Диагностика

| Симптом | Причина | Что делать |
|---|---|---|
| `network postgresql_backend not found` | база в другой сети или сеть не создана | `docker network create postgresql_backend`, `docker network connect ... postgres_db` |
| `password authentication failed` | пароль в `.env` не совпадает с PostgreSQL | исправить `POSTGRES_PASSWORD`, `docker compose up -d` |
| `Ports are not available` | порт занят отладочным запуском | закрыть Visual Studio-запуск или сменить порт в `.env` |
| Identity `unhealthy`, в логах ошибка БД | база не готова или неверные данные | `docker compose logs identity`; проверить сеть и пароль |
| После входа `redirect_uri` mismatch | `PUBLIC_HOST` изменён, клиент не обновился | `docker compose up -d identity`; проверить `OpenIddict:Seed:RefreshClients` в `appsettings.Production.json` |
| Вход зацикливается | открыт `localhost` вместо IP или старые cookie | открыть `http://<IP>:5201`, очистить cookie сайта |
| API `401` | нет токена, истёк или introspection не прошёл | проверить токен и `docker compose logs api` |
| API `invalid_client` | секрет `devhome-api` не совпадает | проверить `API_CLIENT_SECRET`; `docker compose up -d identity api` (пересоздаст контейнеры с новыми переменными) |
| `permission denied` для `/app/App_Data` | том создан старым контейнером с другим владельцем | `docker compose down -v` (ключи будут пересозданы) |
| Сборка `published` падает на `COPY publish/...` | каталог не создан | запустить скрипт с `-Mode published` или `dotnet publish` вручную |
| Docker не может скачать базовый образ | нет сети/DNS | проверить интернет, повторить `docker compose build` |

Полезные проверки:

```powershell
# здоровье приложений
Invoke-RestMethod http://192.168.1.50:5201/health
Invoke-RestMethod http://192.168.1.50:5149/health

# метаданные OIDC
Invoke-RestMethod http://192.168.1.50:5201/.well-known/openid-configuration

# кто в сети
docker network inspect devhome
docker network inspect postgresql_backend
```

## 14. Ограничения и безопасность

- Только HTTP. Трафик не шифруется; используйте только доверенную домашнюю сеть.
- Нет SMTP: подтверждение почты выполняет администратор.
- Нет отдельного reverse proxy и HTTPS; для внешнего доступа это нужно добавить.
- API не опубликован наружу, но доступен любому контейнеру сети `devhome`.
- Самоподписанные сертификаты OpenIddict — для локального запуска, не для интернета.
- Пароли и секреты лежат в `.env`; не коммитьте его и не публикуйте.
- `ADMIN_PASSWORD` нужен только для создания администратора в пустой базе.
  После создания его можно очистить.
- Контейнеры запускаются от непривилегированного пользователя `app`.
- Тома `identity-data` и `web-data` содержат ключи; их резервная копия важна,
  если нужно сохранить сеансы после переустановки.

## 15. Быстрая проверка после запуска

1. `docker compose ps` — все три сервиса `healthy`.
2. `http://<IP>:5201/health` — `200`, `database: ready`, версия.
3. `http://<IP>:5149/health` — `200`, та же версия.
4. Открыть DevHome, нажать «Войти», войти, вернуться на главную.
5. Открыть `/admin` — раздел «Администрирование» доступен администратору.
6. Выполнить команды API из раздела 11: публичный метод `200`, приватный с токеном `200`,
   без токена `401`.
