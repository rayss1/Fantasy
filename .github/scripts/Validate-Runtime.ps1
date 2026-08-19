$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$artifacts = Join-Path $repositoryRoot 'artifacts/runtime-smoke'
$hostOutput = Join-Path $artifacts 'host'
$controlCenterOutput = Join-Path $artifacts 'control-center'

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

dotnet publish (Join-Path $repositoryRoot 'examples/Server/APP/Main/Main.csproj') `
  --configuration Release --framework net10.0 --no-restore --output $hostOutput --nologo
if ($LASTEXITCODE -ne 0) { throw 'Server Host publish failed.' }

$runtimeConfig = Get-Content (Join-Path $hostOutput 'Main.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtimeConfig.runtimeOptions.tfm -ne 'net10.0') {
  throw "Server Host runtime is '$($runtimeConfig.runtimeOptions.tfm)', expected net10.0."
}

$configs = @(Get-ChildItem $hostOutput -Filter Fantasy.config -File -Recurse)
if ($configs.Count -ne 1) {
  throw "Server Host publish contains $($configs.Count) Fantasy.config files; expected exactly one."
}

function Start-LogProbe {
  param(
    [Parameter(Mandatory)] [string] $Assembly,
    [Parameter(Mandatory)] [string] $WorkingDirectory,
    [Parameter(Mandatory)] [string[]] $Arguments,
    [Parameter(Mandatory)] [string] $ExpectedText,
    [Parameter(Mandatory)] [string] $Name
  )

  $stdout = Join-Path $artifacts "$Name.stdout.log"
  $stderr = Join-Path $artifacts "$Name.stderr.log"
  $argumentList = @($Assembly) + $Arguments
  $process = Start-Process dotnet -ArgumentList $argumentList -WorkingDirectory $WorkingDirectory `
    -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru

  try {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    while ([DateTime]::UtcNow -lt $deadline) {
      if ($process.HasExited) {
        throw "$Name exited before the smoke probe completed.`n$(Get-Content $stdout -Raw -ErrorAction SilentlyContinue)`n$(Get-Content $stderr -Raw -ErrorAction SilentlyContinue)"
      }

      $text = (Get-Content $stdout -Raw -ErrorAction SilentlyContinue) + (Get-Content $stderr -Raw -ErrorAction SilentlyContinue)
      if ($text -match [Regex]::Escape($ExpectedText)) {
        return
      }

      Start-Sleep -Milliseconds 500
      $process.Refresh()
    }

    throw "$Name did not emit '$ExpectedText' before timeout.`n$(Get-Content $stdout -Raw -ErrorAction SilentlyContinue)`n$(Get-Content $stderr -Raw -ErrorAction SilentlyContinue)"
  }
  finally {
    if (-not $process.HasExited) {
      $process.Kill($true)
      $process.WaitForExit()
    }
  }
}

Start-LogProbe -Assembly (Join-Path $hostOutput 'Main.dll') -WorkingDirectory $hostOutput `
  -Arguments @('-m', 'Develop') -ExpectedText 'Startup Complete' -Name 'server-host'

dotnet publish (Join-Path $repositoryRoot 'Fantasy.Packages/Fantasy.ControlCenter/Fantasy.ControlCenter.csproj') `
  --configuration Release --framework net10.0 --no-restore --output $controlCenterOutput --nologo
if ($LASTEXITCODE -ne 0) { throw 'Control Center publish failed.' }

$controlStdout = Join-Path $artifacts 'control-center.stdout.log'
$controlStderr = Join-Path $artifacts 'control-center.stderr.log'
$controlProcess = Start-Process dotnet -ArgumentList (Join-Path $controlCenterOutput 'Fantasy.ControlCenter.dll') `
  -WorkingDirectory $controlCenterOutput -RedirectStandardOutput $controlStdout `
  -RedirectStandardError $controlStderr -PassThru
try {
  $deadline = [DateTime]::UtcNow.AddSeconds(45)
  $responded = $false
  while ([DateTime]::UtcNow -lt $deadline) {
    if ($controlProcess.HasExited) {
      throw "Control Center exited before accepting requests.`n$(Get-Content $controlStdout -Raw -ErrorAction SilentlyContinue)`n$(Get-Content $controlStderr -Raw -ErrorAction SilentlyContinue)"
    }

    try {
      $response = Invoke-WebRequest 'http://127.0.0.1:5277' -TimeoutSec 2 -UseBasicParsing
      if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
        $responded = $true
        break
      }
    }
    catch {
      Start-Sleep -Milliseconds 500
    }
    $controlProcess.Refresh()
  }

  if (-not $responded) { throw 'Control Center did not accept HTTP requests before timeout.' }
  if (-not (Test-Path (Join-Path $controlCenterOutput 'data/fantasy-control.db'))) {
    throw 'Control Center did not initialize its SQLite database.'
  }
}
finally {
  if (-not $controlProcess.HasExited) {
    $controlProcess.Kill($true)
    $controlProcess.WaitForExit()
  }
}
