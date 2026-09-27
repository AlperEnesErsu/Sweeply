# Sweeply'ı kaynak koddan derler, %LOCALAPPDATA%\Programs\Sweeply klasörüne kurar
# ve masaüstüne tek tıkla optimize eden bir kısayol ekler.
# Kullanım: powershell -ExecutionPolicy Bypass -File install.ps1 [-NoShortcut]
param([switch]$NoShortcut)
$ErrorActionPreference = 'Stop'

$dest = Join-Path $env:LOCALAPPDATA 'Programs\Sweeply'
$project = Join-Path $PSScriptRoot 'src\Sweeply\Sweeply.csproj'

# Tek dosya, .NET gerektirmeyen (self-contained) exe: src\Sweeply\Properties\PublishProfiles\win-x64.pubxml
dotnet publish $project -p:PublishProfile=win-x64 -o $dest
if ($LASTEXITCODE -ne 0) { throw 'Derleme başarısız oldu.' }

$exe = Join-Path $dest 'Sweeply.exe'
if (-not $NoShortcut) {
    $desktop = [Environment]::GetFolderPath('DesktopDirectory')
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut((Join-Path $desktop 'Sweeply.lnk'))
    $link.TargetPath = $exe
    $link.Arguments = '--auto'
    $link.WorkingDirectory = $dest
    $link.IconLocation = "$exe,0"
    $link.Description = 'Tek tıkla bilgisayarı optimize et'
    $link.Save()
    "Masaüstü kısayolu oluşturuldu."
}
"Kuruldu: $exe"
