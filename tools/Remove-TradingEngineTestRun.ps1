# Removes the synthetic rows of one HTTP-walkthrough run from the Azure Dev
# database. See docs/rider-local-azure-dev.md for the full workflow.
#
# Preview (default):
#   ./tools/Remove-TradingEngineTestRun.ps1 -Server <dev-sql-fqdn> -Database <dev-db> -RunId <runId>
# Delete:
#   ./tools/Remove-TradingEngineTestRun.ps1 -Server <dev-sql-fqdn> -Database <dev-db> -RunId <runId> -Apply
#
# Requires: sqlcmd (go-sqlcmd) on PATH and 'az login' as an Entra identity with
# a contained user in the database (passwordless Active Directory Default).
# Repeating the script for the same run id is a safe no-op.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Server,

    [Parameter(Mandatory)]
    [string]$Database,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Z0-9]{4,32}$')]
    [string]$RunId,

    [switch]$Apply
)

$ErrorActionPreference = 'Stop'

if ($Server -notmatch '^sql-[a-z0-9-]*dev[a-z0-9-]*\.database\.windows\.net$')
{
    throw "Refusing -Server '$Server': this script only targets the Azure Dev SQL server FQDN."
}

if ($Database -notmatch '^sqldb-tradingengine-dev[a-z0-9-]*$')
{
    throw "Refusing -Database '$Database': this script only targets the Azure Dev trading engine database."
}

if ($null -eq (Get-Command sqlcmd -ErrorAction SilentlyContinue))
{
    throw "sqlcmd was not found on PATH. Install go-sqlcmd (https://github.com/microsoft/go-sqlcmd) or the SQL Server command-line tools."
}

$sqlFile = Join-Path $PSScriptRoot 'DevCleanup.sql'

$mode = 'Previewing'
if ($Apply.IsPresent)
{
    $mode = 'Applying'
}

Write-Host "$mode cleanup for run '$RunId' on $Server/$Database."

$applyValue = [int]$Apply.IsPresent
& sqlcmd `
    -S "tcp:$Server,1433" `
    -d $Database `
    --authentication-method ActiveDirectoryDefault `
    -l 30 `
    -b `
    -i $sqlFile `
    -v "RUN_ID=$RunId" `
    -v "EXPECTED_DATABASE=$Database" `
    -v "APPLY=$applyValue"

if ($LASTEXITCODE -ne 0)
{
    throw "sqlcmd failed with exit code $LASTEXITCODE."
}
