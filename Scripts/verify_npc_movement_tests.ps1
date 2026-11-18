# Path + Solzreed data tests without locking bin\Debug (uses _build_probe + _test_out).
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$probe = Join-Path $root "_build_probe"
$testOut = Join-Path $root "_test_out"

dotnet build (Join-Path $root "AAEmu.Game\AAEmu.Game.csproj") -o $probe
dotnet build (Join-Path $root "AAEmu.UnitTests\AAEmu.UnitTests.csproj") -p:BuildProjectReferences=false -o $testOut
Copy-Item (Join-Path $probe "AAEmu.Game.dll") (Join-Path $testOut "AAEmu.Game.dll") -Force

$patterns = @(
    "Zone129_",
    "SolzreedArthur",
    "PathFollow",
    "DesignerPathSpeed",
    "PathWalker",
    "AiPathHandler",
    "SimulationPathSpeed",
    "AiPathsManagerDesigner",
    "RoamMovement",
    "RoamTarget",
    "RoamLeash",
    "RoamPipe",
    "CombatChase",
    "ReturnMovement",
    "PathPlanning",
    "LegPlanner",
    "WalkerStuck",
    "WalkerStuckPatrol",
    "WalkerCollisionScope",
    "WorldManagerNav",
    "ClientSpawnPipeline",
    "ClientSpawnerRegistration",
    "SolzreedSpawner",
    "NpcFooting",
    "ZoneAiPaths",
    "AiPathsManagerDesigner"
)

dotnet test (Join-Path $root "AAEmu.UnitTests\AAEmu.UnitTests.csproj") `
    -p:OutputPath="$testOut\" `
    -p:AppendTargetFrameworkToOutputPath=false `
    --no-build 2>&1 | Tee-Object -Variable out

$failed = $false
foreach ($p in $patterns) {
    if ($out -match "сбой $p") { $failed = $true; Write-Host "FAILED category: $p" -ForegroundColor Red }
}
if (-not $failed) {
    Write-Host "NPC movement/path test categories: no failures matched filter." -ForegroundColor Green
}
exit $(if ($failed) { 1 } else { 0 })