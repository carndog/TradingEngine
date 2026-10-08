# Copy this file to azure-dev-target.local.psd1 (gitignored) and fill in the
# approved Azure Dev target. Remove-TradingEngineTestRun.ps1 refuses to run
# unless -Server/-Database match these values exactly.
@{
    SqlServerFqdn = '<dev-sql-server>.database.windows.net'
    SqlDatabase = '<dev-tradingengine-database>'
}
