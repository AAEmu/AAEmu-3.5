# Solzreed client spawners: 129 (moang packs), 179 (Arthur AIPath_179_moang_29 + patrol).
$env:AAEMU_CLIENT_SPAWN_ZONES = "129,179"
# $env:AAEMU_CLIENT_NPC_SPAWN = "0"   # выключить .g
# $env:AAEMU_TRIANGLE_NAV = "0"       # fallback A* по узлам .bai

$root = Join-Path $PSScriptRoot ".."
$probe = Join-Path $root "_build_probe"
$game = Join-Path $root "AAEmu.Game"

dotnet build (Join-Path $game "AAEmu.Game.csproj") -o $probe | Out-Host
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Push-Location $probe
try {
    & (Join-Path $probe "AAEmu.Game.exe")
}
finally {
    Pop-Location
}