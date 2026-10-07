# Removes the synthetic rows of one HTTP-walkthrough run from the Azure Dev
# database. See docs/rider-local-azure-dev.md for the full workflow.
#
# Preview (default):
#   ./tools/Remove-TradingEngineTestRun.ps1 -Server <dev-sql-fqdn> -Database <dev-db> -RunId <runId>
# Delete:
#   ./tools/Remove-TradingEngineTestRun.ps1 -Server <dev-sql-fqdn> -Database <dev-db> -RunId <runId> -Apply
#
# Requires: sqlcmd (go-sqlcmd) on PATH, 'az login' as an Entra identity with a
# contained user in the database (passwordless Active Directory Default), and
# tools/azure-dev-target.local.psd1 declaring the approved Azure Dev target —
# copy tools/azure-dev-target.example.psd1 and fill in the real values (the
# file is gitignored). -Server/-Database must match it exactly, so a similarly
# named environment can never be targeted by mistake.
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

$targetFile = Join-Path $PSScriptRoot 'azure-dev-target.local.psd1'
if (-not (Test-Path $targetFile))
{
    throw "Expected Azure Dev target file '$targetFile' not found. Copy tools/azure-dev-target.example.psd1, fill in the approved Azure Dev SQL server FQDN and database name, and rerun."
}

$target = Import-PowerShellDataFile $targetFile
$expectedServerFqdn = [string]$target.SqlServerFqdn
$expectedDatabase = [string]$target.SqlDatabase

if ([string]::IsNullOrWhiteSpace($expectedServerFqdn) -or [string]::IsNullOrWhiteSpace($expectedDatabase))
{
    throw "tools/azure-dev-target.local.psd1 must declare non-empty SqlServerFqdn and SqlDatabase values."
}

if ($Server -ne $expectedServerFqdn)
{
    throw "Refusing -Server '$Server': it is not the configured Azure Dev target server '$expectedServerFqdn' from tools/azure-dev-target.local.psd1."
}

if ($Database -ne $expectedDatabase)
{
    throw "Refusing -Database '$Database': it is not the configured Azure Dev target database '$expectedDatabase' from tools/azure-dev-target.local.psd1."
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

# @@SERVERNAME on Azure SQL is the logical server name, not the FQDN.
$expectedServerName = $expectedServerFqdn.Split('.')[0]

$applyValue = [int]$Apply.IsPresent
& sqlcmd `
    -S "tcp:$Server,1433" `
    -d $Database `
    --authentication-method ActiveDirectoryDefault `
    -l 30 `
    -b `
    -i $sqlFile `
    -v "RUN_ID=$RunId" `
    -v "EXPECTED_SERVER=$expectedServerName" `
    -v "EXPECTED_DATABASE=$Database" `
    -v "APPLY=$applyValue"

if ($LASTEXITCODE -ne 0)
{
    throw "sqlcmd failed with exit code $LASTEXITCODE."
}
