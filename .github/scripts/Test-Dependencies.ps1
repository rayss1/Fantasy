$ErrorActionPreference = 'Stop'

function Test-HasVulnerabilities {
  param([object] $Value)

  if ($null -eq $Value -or $Value -is [string]) { return $false }
  if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [pscustomobject]) {
    foreach ($item in $Value) {
      if (Test-HasVulnerabilities $item) { return $true }
    }
    return $false
  }

  foreach ($property in $Value.PSObject.Properties) {
    if ($property.Name -eq 'vulnerabilities' -and @($property.Value).Count -gt 0) { return $true }
    if (Test-HasVulnerabilities $property.Value) { return $true }
  }
  return $false
}

foreach ($solution in @('Fantasy.sln', 'examples/Server/Server.sln')) {
  $output = dotnet package list --project $solution --vulnerable --include-transitive --no-restore --format json --output-version 1
  if ($LASTEXITCODE -ne 0) {
    throw "Dependency audit failed for $solution."
  }
  $result = $output | ConvertFrom-Json -Depth 100
  if (Test-HasVulnerabilities $result) {
    throw "Dependency audit reported a known vulnerability in $solution."
  }
}
