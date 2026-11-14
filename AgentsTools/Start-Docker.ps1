#Requires -Version 5.1

<#
.SYNOPSIS
    Собирает и запускает DevHome, DevHome.Identity и шаблонное API в Docker Desktop.

.DESCRIPTION
    Скрипт делает всю подготовку, чтобы не редактировать docker-compose.yml вручную:
      1. проверяет, что Docker Desktop запущен;
      2. создаёт .env из .env.example (если файла ещё нет);
      3. записывает версию, режим сборки и адрес в локальной сети;
      4. в режиме published публикует приложения в publish/;
      5. вызывает docker compose up -d --build.

    База данных не запускается: используется существующий контейнер postgres_db
    в сети postgresql_backend. Подробности — в AgentsDocs/docker.md.

.PARAMETER Version
    Версия сборки, по умолчанию 0.1.0. Попадает в тег образа и в /health.

.PARAMETER Mode
    source    — собрать из исходников внутри Docker (по умолчанию);
    published — взять готовые приложения из publish/.

.PARAMETER PublicHost
    IPv4-адрес компьютера в локальной сети (например, 192.168.1.50).
    Если не указан, скрипт попробует определить адрес автоматически.
    Не указывайте http:// и порт.

.PARAMETER NoBuild
    Не пересобирать образы, только запустить уже собранные.

.PARAMETER Interactive
    Показать меню выбора режима и версии. Используется в Build-Docker.cmd.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Start-Docker.ps1 -PublicHost 192.168.1.50

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Start-Docker.ps1 -Version 0.2.0 -Mode published -PublicHost 192.168.1.50
#>
[CmdletBinding()]
param(
    [string]$Version = '0.1.0',

    [ValidateSet('source', 'published')]
    [string]$Mode = 'source',

    [string]$PublicHost,

    [switch]$NoBuild,

    [switch]$Interactive
)

chcp 65001 > $null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

trap {
    Write-Host ''
    Write-Host "ОШИБКА: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

$root = Split-Path -Parent $PSScriptRoot
$envPath = Join-Path $root '.env'
$examplePath = Join-Path $root '.env.example'

function Set-EnvValue {
    param(
        [System.Collections.Generic.List[string]]$Lines,
        [string]$Key,
        [string]$Value
    )

    $pattern = '^' + [regex]::Escape($Key) + '='

    for ($index = 0; $index -lt $Lines.Count; $index++) {
        if ($Lines[$index] -match $pattern) {
            $Lines[$index] = "$Key=$Value"
            return
        }
    }

    $Lines.Add("$Key=$Value")
}

function Get-EnvValue {
    param(
        [System.Collections.Generic.List[string]]$Lines,
        [string]$Key
    )

    $pattern = '^' + [regex]::Escape($Key) + '=(.*)$'

    foreach ($line in $Lines) {
        if ($line -match $pattern) {
            return $Matches[1].Trim()
        }
    }

    return $null
}

function Find-LanAddress {
    $address = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object {
            $_.IPAddress -notmatch '^(127\.|169\.254\.|172\.(1[7-9]|2[0-9]|3[0-1])\.)' -and
            $_.InterfaceAlias -notmatch 'Loopback|vEthernet|Docker|WSL'
        } |
        Sort-Object -Property InterfaceMetric |
        Select-Object -First 1 -ExpandProperty IPAddress

    return $address
}

function Assert-HostValue {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw 'Не задан PUBLIC_HOST. Укажите IPv4-адрес компьютера в локальной сети: -PublicHost 192.168.1.50'
    }

    if ($Value -match '^[a-zA-Z][a-zA-Z0-9+.-]*://' -or $Value.Contains('/') -or $Value.Contains(':')) {
        throw "PUBLIC_HOST должен быть адресом или именем без схемы и порта. Получено: '$Value'"
    }
}

# --- 1. Docker ---------------------------------------------------------------
& docker info *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'Docker Desktop не запущен или недоступен. Запустите Docker Desktop и повторите.'
}

& docker compose version *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'Не найден плагин docker compose. Обновите Docker Desktop.'
}

# --- 2. .env -----------------------------------------------------------------
if (-not (Test-Path -LiteralPath $envPath)) {
    if (-not (Test-Path -LiteralPath $examplePath)) {
        throw "Не найден шаблон .env.example в каталоге $root"
    }

    Copy-Item -LiteralPath $examplePath -Destination $envPath
    Write-Host "Создан .env из .env.example: $envPath"
}

$lines = [System.Collections.Generic.List[string]]::new()
foreach ($line in (Get-Content -LiteralPath $envPath -Encoding UTF8)) {
    $lines.Add([string]$line)
}

if ([string]::IsNullOrWhiteSpace($PublicHost)) {
    $PublicHost = Get-EnvValue -Lines $lines -Key 'PUBLIC_HOST'
}

if ([string]::IsNullOrWhiteSpace($PublicHost) -or
    $PublicHost -eq 'localhost' -or
    $PublicHost -eq '192.168.1.100') {
    $detected = Find-LanAddress
    if ($detected) {
        $PublicHost = $detected
        Write-Host "PUBLIC_HOST определён автоматически: $PublicHost"
    }
}

if ($Interactive) {
    Write-Host ''
    Write-Host 'Что сделать?'
    Write-Host '  1 — собрать из исходников и запустить (source)'
    Write-Host '  2 — опубликовать приложения и собрать из publish (published)'
    Write-Host '  0 — выйти'

    $choice = Read-Host 'Выбор [1]'
    if ([string]::IsNullOrWhiteSpace($choice)) {
        $choice = '1'
    }

    switch ($choice) {
        '1' { $Mode = 'source' }
        '2' { $Mode = 'published' }
        '0' { return }
        default { throw "Неизвестный выбор: $choice. Введите 1, 2 или 0." }
    }

    $enteredVersion = Read-Host "Версия [$Version]"
    if (-not [string]::IsNullOrWhiteSpace($enteredVersion)) {
        $Version = $enteredVersion.Trim()
    }

    $enteredHost = Read-Host "PUBLIC_HOST [$PublicHost]"
    if (-not [string]::IsNullOrWhiteSpace($enteredHost)) {
        $PublicHost = $enteredHost.Trim()
    }
}

Assert-HostValue -Value $PublicHost

Set-EnvValue -Lines $lines -Key 'PUBLIC_HOST' -Value $PublicHost
Set-EnvValue -Lines $lines -Key 'APP_VERSION' -Value $Version
Set-EnvValue -Lines $lines -Key 'BUILD_MODE' -Value $Mode

[System.IO.File]::WriteAllLines(
    $envPath,
    $lines,
    (New-Object System.Text.UTF8Encoding($false)))

# Переменные окружения имеют приоритет над .env, поэтому выставляем и их:
# иначе старое значение из прошлого запуска перебьёт только что записанное.
$env:PUBLIC_HOST = $PublicHost
$env:APP_VERSION = $Version
$env:BUILD_MODE = $Mode

# --- 3. Публикация для режима published --------------------------------------
if ($Mode -eq 'published') {
    $publishRoot = Join-Path $root 'publish'
    $expectedPublishRoot = [System.IO.Path]::GetFullPath($publishRoot)
    $expectedRoot = [System.IO.Path]::GetFullPath($root)

    if (-not $expectedPublishRoot.StartsWith($expectedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Небезопасный путь публикации: $expectedPublishRoot"
    }

    if (Test-Path -LiteralPath $publishRoot) {
        Remove-Item -LiteralPath $publishRoot -Recurse -Force
    }

    $applications = @(
        @{ Project = 'DevHome.Identity/DevHome.Identity/DevHome.Identity.csproj'; Output = 'Identity' },
        @{ Project = 'DevHome/DevHome/DevHome.csproj'; Output = 'Web' },
        @{ Project = 'DevHome/DevHome.Api/DevHome.Api.csproj'; Output = 'Api' }
    )

    foreach ($application in $applications) {
        $outputPath = Join-Path $publishRoot $application.Output
        Write-Host "Публикация $($application.Project) -> $outputPath"

        & dotnet publish (Join-Path $root $application.Project) `
            -c Release `
            -o $outputPath `
            -p:UseAppHost=false `
            "-p:Version=$Version" `
            "-p:InformationalVersion=$Version" `
            "-p:IncludeSourceRevisionInInformationalVersion=false"

        if ($LASTEXITCODE -ne 0) {
            throw "Не удалось опубликовать $($application.Project)"
        }
    }
}

# --- 4. Запуск ---------------------------------------------------------------
Push-Location $root
try {
    if ($NoBuild) {
        Write-Host 'Запуск без пересборки: docker compose up -d'
        & docker compose up -d
    }
    else {
        Write-Host "Сборка и запуск: версия $Version, режим $Mode"
        & docker compose up -d --build
    }

    if ($LASTEXITCODE -ne 0) {
        throw 'docker compose завершился с ошибкой. Посмотрите вывод выше.'
    }

    Write-Host ''
    Write-Host 'Готово. Адреса в локальной сети:'
    Write-Host "  Identity: http://${PublicHost}:5201/"
    Write-Host "  DevHome:  http://${PublicHost}:5149/"
    Write-Host "  API:      только внутри Docker (наружу не опубликован)"
    Write-Host ''
    Write-Host 'Состояние: docker compose ps'
    Write-Host 'Журналы:   docker compose logs -f identity web api'
    Write-Host 'Остановка: docker compose down'
}
finally {
    Pop-Location
}
