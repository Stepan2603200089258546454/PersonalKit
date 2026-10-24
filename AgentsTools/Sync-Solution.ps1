#Requires -Version 5.1

<#
.SYNOPSIS
    Синхронизирует файл решения с файловой системой рабочей области.

.DESCRIPTION
    Скрипт управляет только теми элементами решения, которые создаёт сам:
      * файлы в корне рабочей области — попадают в папку решения «Элементы решения»;
      * файлы и каталоги AgentsDocs/ и AgentsTools/ — повторяют физическую структуру.

    Каталоги проектов не синхронизируются: их структура в обозревателе решений
    поддерживается вручную (Visual Studio, dotnet sln add). Скрипт никогда не перемещает
    и не удаляет проекты, их папки и вложенные в них элементы.

    Если проект есть на диске, но отсутствует в решении, скрипт только предупреждает об этом.

.PARAMETER SolutionPath
    Путь к файлу решения. По умолчанию берётся единственный *.slnx в корне рабочей области.

.PARAMETER Verify
    Ничего не записывает: сравнивает решение с файловой системой.
    Код 0 — синхронизировано, 1 — есть расхождения, 2 — ошибка.

.PARAMETER DryRun
    Выводит новое содержимое решения в stdout, не записывая файл.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Sync-Solution.ps1
    Записывает актуальную структуру решения.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Sync-Solution.ps1 -Verify
    Проверяет, что управляемые элементы решения соответствуют файловой системе.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$SolutionPath,

    [switch]$Verify,

    [switch]$DryRun
)

chcp 65001 > $null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# В Windows PowerShell сборка System.Xml.Linq не загружена по умолчанию.
Add-Type -AssemblyName System.Xml.Linq

# Корень рабочей области — каталог над AgentsTools.
$script:Root = Split-Path -Parent $PSScriptRoot

# Имя папки решения для файлов, лежащих рядом с файлом решения.
$script:SolutionItemsFolderName = 'Элементы решения'

# Каталоги, содержимое которых синхронизируется (пока только каталоги агента).
$script:AgentFolderNames = @('AgentsDocs', 'AgentsTools')

$script:IgnoredDirectoryNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($name in @('.vs', '.git', '.idea', '.vscode', 'bin', 'obj', 'node_modules', 'temp', 'artifacts', 'TestResults', 'packages')) {
    [void]$script:IgnoredDirectoryNames.Add($name)
}

$script:IgnoredExtensions = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($extension in @('.user', '.suo', '.tmp', '.log', '.binlog', '.bak', '.orig', '.sln', '.slnx')) {
    [void]$script:IgnoredExtensions.Add($extension)
}

$script:ProjectExtensions = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($extension in @('.csproj', '.fsproj', '.vbproj', '.vcxproj', '.sqlproj', '.esproj', '.dcproj', '.wixproj', '.proj')) {
    [void]$script:ProjectExtensions.Add($extension)
}

function New-XElement {
    param([string]$Name)
    return [System.Xml.Linq.XElement]::new([System.Xml.Linq.XName]::Get($Name))
}

function New-XAttributeName {
    param([string]$Name)
    return [System.Xml.Linq.XName]::Get($Name)
}

function Get-RelativeWorkspacePath {
    param([string]$FullName)
    $prefix = $script:Root.TrimEnd('\') + '\'
    return $FullName.Substring($prefix.Length).Replace('\', '/')
}

function Get-ParentDirectoryPath {
    param([string]$Path)
    $index = $Path.LastIndexOf('/')
    if ($index -lt 0) { return '' }
    return $Path.Substring(0, $index)
}

function Format-DirectoryDisplay {
    param([string]$Directory)
    if ($Directory -eq '') { return 'корень решения' }
    return "/$Directory/"
}

function Resolve-SolutionPath {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        $files = @(Get-ChildItem -LiteralPath $script:Root -Filter '*.slnx' -File -Force)
        if ($files.Count -eq 0) {
            throw "В корне рабочей области не найден файл *.slnx ($($script:Root))."
        }
        if ($files.Count -gt 1) {
            throw 'В корне рабочей области несколько файлов *.slnx — укажите нужный через -SolutionPath.'
        }
        return $files[0].FullName
    }
    if (-not [System.IO.Path]::IsPathRooted($Path)) {
        $Path = Join-Path $script:Root $Path
    }
    return [System.IO.Path]::GetFullPath($Path)
}

function Test-IgnoredFile {
    param([System.IO.FileInfo]$File, [string]$RelativePath)

    if ($script:IgnoredExtensions.Contains($File.Extension)) { return $true }
    if ($File.Name.StartsWith('~$', [System.StringComparison]::Ordinal)) { return $true }

    $segments = $RelativePath.Split('/')
    for ($index = 0; $index -lt $segments.Length - 1; $index++) {
        if ($script:IgnoredDirectoryNames.Contains($segments[$index])) { return $true }
    }
    return $false
}

function Test-ManagedFilePath {
    param([string]$Path)

    $normalized = $Path.Replace('\', '/').TrimStart('/')
    if ($normalized -eq '') { return $false }

    # Файл в корне рабочей области — элемент папки «Элементы решения».
    if (-not $normalized.Contains('/')) { return $true }

    foreach ($name in $script:AgentFolderNames) {
        if ($normalized.StartsWith("$name/", [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Test-ManagedFolderPath {
    param([string]$Path)

    $normalized = $Path.Trim('/')
    if ($normalized -ieq $script:SolutionItemsFolderName) { return $true }

    foreach ($name in $script:AgentFolderNames) {
        if ($normalized -ieq $name) { return $true }
        if ($normalized.StartsWith("$name/", [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Get-ManagedModel {
    param([string]$SolutionFullPath)

    $rootFiles = [System.Collections.Generic.List[string]]::new()
    $agentFiles = [System.Collections.Generic.SortedDictionary[string, object]]::new([System.StringComparer]::Ordinal)

    foreach ($file in (Get-ChildItem -LiteralPath $script:Root -File -Force)) {
        if ($file.FullName -ieq $SolutionFullPath) { continue }
        $relative = Get-RelativeWorkspacePath $file.FullName
        if (Test-IgnoredFile -File $file -RelativePath $relative) { continue }
        if ($script:ProjectExtensions.Contains($file.Extension)) { continue }
        $rootFiles.Add($relative)
    }

    foreach ($agentFolder in $script:AgentFolderNames) {
        $agentPath = Join-Path $script:Root $agentFolder
        if (-not (Test-Path -LiteralPath $agentPath)) { continue }

        foreach ($file in (Get-ChildItem -LiteralPath $agentPath -Recurse -File -Force)) {
            $relative = Get-RelativeWorkspacePath $file.FullName
            if (Test-IgnoredFile -File $file -RelativePath $relative) { continue }
            if ($script:ProjectExtensions.Contains($file.Extension)) { continue }

            $directory = Get-ParentDirectoryPath $relative
            if (-not $agentFiles.ContainsKey($directory)) {
                $agentFiles[$directory] = [System.Collections.Generic.List[string]]::new()
            }
            $agentFiles[$directory].Add($relative)
        }
    }

    # Родительские каталоги добавляются пустыми записями, чтобы структура папок была полной.
    foreach ($directory in @($agentFiles.Keys)) {
        $current = Get-ParentDirectoryPath $directory
        while ($current -ne '') {
            if (-not $agentFiles.ContainsKey($current)) {
                $agentFiles[$current] = [System.Collections.Generic.List[string]]::new()
            }
            $current = Get-ParentDirectoryPath $current
        }
    }

    return [pscustomobject]@{
        RootFiles  = $rootFiles
        AgentFiles = $agentFiles
    }
}

function Get-ProjectFiles {
    $projects = [System.Collections.Generic.List[string]]::new()
    foreach ($file in (Get-ChildItem -LiteralPath $script:Root -Recurse -File -Force)) {
        if (-not $script:ProjectExtensions.Contains($file.Extension)) { continue }
        $relative = Get-RelativeWorkspacePath $file.FullName
        if (Test-IgnoredFile -File $file -RelativePath $relative) { continue }
        $projects.Add($relative)
    }
    return $projects
}

function Get-SolutionProjectPaths {
    param([string]$SolutionFullPath)

    $paths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    if (-not (Test-Path -LiteralPath $SolutionFullPath)) { return $paths }

    $document = [System.Xml.Linq.XDocument]::Load($SolutionFullPath)
    foreach ($element in $document.Descendants()) {
        if ($element.Name.LocalName -ne 'Project') { continue }
        $pathAttribute = $element.Attribute((New-XAttributeName 'Path'))
        if ($null -eq $pathAttribute) { continue }
        [void]$paths.Add($pathAttribute.Value.Replace('\', '/').TrimStart('/'))
    }
    return $paths
}

function Get-FolderPathsByElement {
    param([System.Xml.Linq.XElement]$RootElement)

    $map = [System.Collections.Generic.Dictionary[System.Xml.Linq.XElement, string]]::new()

    function Visit-Folder {
        param($Element, [string]$Prefix)
        foreach ($child in $Element.Elements()) {
            if ($child.Name.LocalName -ne 'Folder') { continue }
            $nameAttribute = $child.Attribute((New-XAttributeName 'Name'))
            if ($null -eq $nameAttribute) { continue }

            $name = $nameAttribute.Value
            $fullName = if ($name.StartsWith('/', [System.StringComparison]::Ordinal)) { $name } else { $Prefix + $name }
            if (-not $fullName.EndsWith('/', [System.StringComparison]::Ordinal)) { $fullName += '/' }

            $map[$child] = $fullName.Trim('/')
            Visit-Folder -Element $child -Prefix $fullName
        }
    }

    Visit-Folder -Element $RootElement -Prefix ''
    return $map
}

function Remove-ManagedEntries {
    param([System.Xml.Linq.XElement]$RootElement)

    $folderPaths = Get-FolderPathsByElement -RootElement $RootElement

    $fileElements = @($RootElement.Descendants() | Where-Object { $_.Name.LocalName -eq 'File' })
    foreach ($element in $fileElements) {
        $pathAttribute = $element.Attribute((New-XAttributeName 'Path'))
        if ($null -eq $pathAttribute) { continue }
        if (-not (Test-ManagedFilePath -Path $pathAttribute.Value)) { continue }
        if ($null -ne $element.Parent) { $element.Remove() }
    }

    $managedFolders = @($RootElement.Descendants() | Where-Object { $_.Name.LocalName -eq 'Folder' })
    foreach ($element in $managedFolders) {
        if (-not $folderPaths.ContainsKey($element)) { continue }
        if (-not (Test-ManagedFolderPath -Path $folderPaths[$element])) { continue }
        if ($null -ne $element.Parent) { $element.Remove() }
    }
}

function Add-ManagedEntries {
    param([System.Xml.Linq.XElement]$RootElement, $Model)

    if ($Model.RootFiles.Count -gt 0) {
        $folderElement = New-XElement 'Folder'
        $folderElement.SetAttributeValue((New-XAttributeName 'Name'), '/' + $script:SolutionItemsFolderName + '/')
        foreach ($path in @($Model.RootFiles | Sort-Object -CaseSensitive)) {
            [void]$folderElement.Add((New-FileElement -Path $path))
        }
        [void]$RootElement.Add($folderElement)
    }

    $folderElements = [System.Collections.Generic.Dictionary[string, System.Xml.Linq.XElement]]::new([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($directory in $Model.AgentFiles.Keys) {
        $parent = Get-ParentDirectoryPath $directory
        $leaf = $directory.Substring($directory.LastIndexOf('/') + 1)
        $name = if ($parent -eq '') { "/$leaf/" } else { "$leaf/" }

        $element = New-XElement 'Folder'
        $element.SetAttributeValue((New-XAttributeName 'Name'), $name)

        if ($parent -eq '') {
            [void]$RootElement.Add($element)
        }
        else {
            [void]$folderElements[$parent].Add($element)
        }
        $folderElements[$directory] = $element

        foreach ($path in @($Model.AgentFiles[$directory] | Sort-Object -CaseSensitive)) {
            [void]$element.Add((New-FileElement -Path $path))
        }
    }
}

function New-FileElement {
    param([string]$Path)

    $element = New-XElement 'File'
    $element.SetAttributeValue((New-XAttributeName 'Path'), $Path)
    return $element
}

function ConvertTo-SolutionText {
    param([System.Xml.Linq.XElement]$RootElement)

    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Indent = $true
    $settings.IndentChars = '  '
    $settings.NewLineChars = "`r`n"
    $settings.NewLineHandling = [System.Xml.NewLineHandling]::None
    $settings.OmitXmlDeclaration = $true

    $builder = [System.Text.StringBuilder]::new()
    $writer = [System.Xml.XmlWriter]::Create($builder, $settings)
    try {
        $RootElement.Save($writer)
    }
    finally {
        $writer.Dispose()
    }
    return $builder.ToString()
}

function Get-SolutionEncoding {
    # Решение всегда записывается в UTF-8 с BOM: так его одинаково читают
    # Visual Studio, MSBuild и Windows PowerShell 5.1 (Get-Content).
    return [System.Text.UTF8Encoding]::new($true)
}

function Get-ManagedEntriesFromElement {
    param([System.Xml.Linq.XElement]$RootElement)

    $entries = [System.Collections.Generic.List[object]]::new()

    function Visit-Container {
        param($Element, [string]$Prefix)

        foreach ($child in $Element.Elements()) {
            $localName = $child.Name.LocalName

            if ($localName -eq 'Folder') {
                $nameAttribute = $child.Attribute((New-XAttributeName 'Name'))
                if ($null -eq $nameAttribute) { continue }
                $name = $nameAttribute.Value
                $fullName = if ($name.StartsWith('/', [System.StringComparison]::Ordinal)) { $name } else { $Prefix + $name }
                if (-not $fullName.EndsWith('/', [System.StringComparison]::Ordinal)) { $fullName += '/' }
                Visit-Container -Element $child -Prefix $fullName
            }
            elseif ($localName -eq 'File') {
                $pathAttribute = $child.Attribute((New-XAttributeName 'Path'))
                if ($null -eq $pathAttribute) { continue }
                $path = $pathAttribute.Value.Replace('\', '/').TrimStart('/')
                if (-not (Test-ManagedFilePath -Path $path)) { continue }
                $entries.Add([pscustomobject]@{ Directory = $Prefix.Trim('/'); Path = $path })
            }
        }
    }

    Visit-Container -Element $RootElement -Prefix ''
    return $entries
}

function Test-EntryExists {
    param($Entries, [string]$Directory, [string]$Path)
    foreach ($entry in $Entries) {
        if ($entry.Directory.Equals($Directory, [System.StringComparison]::OrdinalIgnoreCase) -and
            $entry.Path.Equals($Path, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

function Get-ExpectedEntries {
    param($Model)

    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($path in $Model.RootFiles) {
        $entries.Add([pscustomobject]@{ Directory = $script:SolutionItemsFolderName; Path = $path })
    }
    foreach ($directory in $Model.AgentFiles.Keys) {
        foreach ($path in $Model.AgentFiles[$directory]) {
            $entries.Add([pscustomobject]@{ Directory = $directory; Path = $path })
        }
    }
    return $entries
}

function Write-Changes {
    param($Changes, [string]$Header)
    if ($Changes.Count -eq 0) { return }

    Write-Host $Header
    foreach ($change in $Changes) {
        Write-Host ("    {0} {1} → папка {2}" -f $change.Kind, $change.Path, (Format-DirectoryDisplay $change.Directory))
    }
}

function Write-ProjectWarning {
    param($MissingProjects)
    if ($MissingProjects.Count -eq 0) { return }

    Write-Host 'Внимание: проекты есть на диске, но отсутствуют в решении (синхронизация их не добавляет):'
    foreach ($project in $MissingProjects) {
        Write-Host "    $project"
    }
    Write-Host 'Добавьте вручную: dotnet sln add <путь к проекту>'
}

try {
    $solutionFullPath = Resolve-SolutionPath $SolutionPath
    $solutionName = [System.IO.Path]::GetFileName($solutionFullPath)

    $model = Get-ManagedModel -SolutionFullPath $solutionFullPath
    $solutionProjects = Get-SolutionProjectPaths -SolutionFullPath $solutionFullPath
    $missingProjects = @(Get-ProjectFiles | Where-Object { -not $solutionProjects.Contains($_) } | Sort-Object)

    $rootCount = $model.RootFiles.Count
    $agentCount = 0
    foreach ($list in $model.AgentFiles.Values) { $agentCount += $list.Count }

    if ($Verify) {
        if (-not (Test-Path -LiteralPath $solutionFullPath)) {
            throw "Файл решения не найден: $solutionFullPath"
        }

        $document = [System.Xml.Linq.XDocument]::Load($solutionFullPath)
        $actualEntries = Get-ManagedEntriesFromElement -RootElement $document.Root
        $expectedEntries = Get-ExpectedEntries -Model $model

        $missing = [System.Collections.Generic.List[object]]::new()
        $extra = [System.Collections.Generic.List[object]]::new()

        foreach ($entry in $expectedEntries) {
            if (-not (Test-EntryExists -Entries $actualEntries -Directory $entry.Directory -Path $entry.Path)) {
                $missing.Add([pscustomobject]@{ Kind = 'файл'; Path = $entry.Path; Directory = $entry.Directory })
            }
        }
        foreach ($entry in $actualEntries) {
            if (-not (Test-EntryExists -Entries $expectedEntries -Directory $entry.Directory -Path $entry.Path)) {
                $extra.Add([pscustomobject]@{ Kind = 'файл'; Path = $entry.Path; Directory = $entry.Directory })
            }
        }

        if ($missing.Count -eq 0 -and $extra.Count -eq 0) {
            Write-Host "Решение синхронизировано: $solutionName (корневых файлов: $rootCount, файлов агента: $agentCount)."
            Write-ProjectWarning -MissingProjects $missingProjects
            exit 0
        }

        Write-Host "Решение $solutionName рассинхронизировано с файловой системой."
        Write-Changes -Changes $missing -Header '  отсутствует:'
        Write-Changes -Changes $extra -Header '  лишнее:'
        Write-ProjectWarning -MissingProjects $missingProjects
        Write-Host 'Запустите: powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Sync-Solution.ps1'
        exit 1
    }

    $document = if (Test-Path -LiteralPath $solutionFullPath) {
        [System.Xml.Linq.XDocument]::Load($solutionFullPath)
    }
    else {
        [System.Xml.Linq.XDocument]::new((New-XElement 'Solution'))
    }

    Remove-ManagedEntries -RootElement $document.Root
    Add-ManagedEntries -RootElement $document.Root -Model $model

    $text = ConvertTo-SolutionText -RootElement $document.Root

    if ($DryRun) {
        [Console]::Out.Write($text)
        exit 0
    }

    $encoding = Get-SolutionEncoding

    $currentText = $null
    if (Test-Path -LiteralPath $solutionFullPath) {
        $currentText = [System.IO.File]::ReadAllText($solutionFullPath)
    }

    if ($null -ne $currentText -and $currentText -ceq $text) {
        Write-Host "Решение уже синхронизировано: $solutionName (корневых файлов: $rootCount, файлов агента: $agentCount)."
        Write-ProjectWarning -MissingProjects $missingProjects
        exit 0
    }

    [System.IO.File]::WriteAllText($solutionFullPath, $text, $encoding)
    Write-Host "Решение записано: $solutionFullPath"
    Write-Host "  корневых файлов: $rootCount, файлов агента: $agentCount"
    Write-ProjectWarning -MissingProjects $missingProjects
    exit 0
}
catch {
    [Console]::Error.WriteLine("Ошибка: $($_.Exception.Message)")
    exit 2
}
