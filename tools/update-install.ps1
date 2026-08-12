<#
.SYNOPSIS
    Обновляет портативную установку Erdtree Keeper, не трогая данные пользователя.

.DESCRIPTION
    Заменяет только файлы программы: exe, нативные библиотеки и документацию.
    Настройки, снимки и автосохранения остаются на месте.

    Скрипт существует потому, что "просто перезаписать папку" уже приводило к
    потере настроек: удаление папки целиком уносит с собой и
    erdtree-keeper.settings.json, и папку со снимками, которая по умолчанию
    лежит внутри.

.EXAMPLE
    ./tools/update-install.ps1 -Target 'D:\Программы\ErdtreeKeeper'
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Target,

    # Папка со свежесобранными файлами (результат dotnet publish).
    [string] $Source = (Join-Path $PSScriptRoot '..\out')
)

$ErrorActionPreference = 'Stop'

$Source = (Resolve-Path $Source).Path
if (-not (Test-Path $Source)) { throw "Нет папки сборки: $Source" }

$exe = Join-Path $Source 'ErdtreeKeeper.exe'
if (-not (Test-Path $exe)) { throw "В $Source нет ErdtreeKeeper.exe - сначала соберите проект" }

if (-not (Test-Path $Target)) {
    New-Item -ItemType Directory -Path $Target | Out-Null
    Write-Host "Создана папка $Target"
}

# Запущенная программа держит свои библиотеки, заменить их не выйдет.
$running = Get-Process -Name ErdtreeKeeper -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and (Split-Path $_.Path -Parent) -eq (Resolve-Path $Target).Path }
if ($running) {
    throw "Программа запущена из $Target - закройте её и повторите"
}

# Обновляем только своё. Всё остальное в папке принадлежит пользователю.
$programFiles = @('ErdtreeKeeper.exe', '*.dll', 'README.md', 'SECURITY.md', 'LICENSE', 'SHA256SUMS.txt')

foreach ($pattern in $programFiles) {
    Get-ChildItem -Path $Source -Filter $pattern -File -ErrorAction SilentlyContinue |
        Copy-Item -Destination $Target -Force
}

# Контрольные суммы считаем по тому, что реально легло в папку.
Get-ChildItem -Path $Target -Include '*.exe', '*.dll' -Recurse -Depth 0 | ForEach-Object {
    "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)"
} | Out-File -FilePath (Join-Path $Target 'SHA256SUMS.txt') -Encoding utf8

$settings = Join-Path $Target 'erdtree-keeper.settings.json'
$kept = if (Test-Path $settings) { 'настройки на месте' } else { 'настроек не было' }

Write-Host "Обновлено: $Target"
Write-Host "Данные пользователя не тронуты - $kept"
