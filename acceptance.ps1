#Requires -Version 5.1
<#
.SYNOPSIS
  Local browser acceptance harness for the watched-instrument catalogue.

  Seeds synthetic data (direct REST, plus direct SQL for the scale fixture),
  then drives headless Edge through the Chrome DevTools Protocol to exercise
  the rendered UI. Synthetic data only; intended for the local API on :5068
  with a disposable local SQL instance.

  REQUIRES -LocalDb or $env:TE_ACCEPTANCE_DB set to the disposable local SQL
  connection string. The connection string is parsed with the SQL provider's
  own connection-string builder; the resolved Data Source must be a
  whitelisted local host and the database name must match the connected
  database. A direct-SQL probe row must then be visible through the API
  before any API mutation is attempted.

  All records created by a run carry a unique run tag and are cleaned up by
  GUID/tag in the finally block. Unrelated pre-existing rows are snapshotted
  (id + rowversion) and verified unchanged at the end.
#>
param(
    [string]$BaseUrl = 'http://localhost:5068',
    [string]$LocalDb = $env:TE_ACCEPTANCE_DB,
    [int]$ScaleRows = 1000,
    [int]$DebugPort = 9333,
    [string]$InjectFault = ''
)

$ErrorActionPreference = 'Stop'
$script:results = [System.Collections.Generic.List[object]]::new()
$script:cleanupFailures = [System.Collections.Generic.List[string]]::new()

function Check {
    param([string]$Name, [bool]$Ok)
    $script:results.Add([pscustomobject]@{ Check = $Name; Result = $(if ($Ok) { 'PASS' } else { 'FAIL' }) })
    Write-Host "$(if ($Ok) { 'PASS' } else { 'FAIL' }) $Name"
}

# ---------- guards and helpers ----------

if ([string]::IsNullOrWhiteSpace($LocalDb)) {
    throw 'Refusing to run: set -LocalDb or $env:TE_ACCEPTANCE_DB to the disposable local SQL connection string.'
}

$csb = $null
try {
    $csb = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($LocalDb)
} catch {
    throw "Refusing to run: -LocalDb is not a valid SQL connection string ($($_.Exception.Message))."
}
if ([string]::IsNullOrWhiteSpace($csb.InitialCatalog)) {
    throw 'Refusing to run: -LocalDb must name a specific disposable database (Initial Catalog).'
}

function Resolve-SqlHost {
    param([string]$DataSource)
    $hostPart = $DataSource.Trim().ToLowerInvariant()
    if ($hostPart -match '^(tcp|np|lpc|via):') {
        $hostPart = $hostPart -replace '^[a-z]+:', ''
    }
    if ($hostPart.StartsWith('[')) {
        $end = $hostPart.IndexOf(']')
        if ($end -gt 0) { return $hostPart.Substring(1, $end - 1) }
        return $hostPart
    }
    $slash = $hostPart.IndexOf('\')
    if ($slash -ge 0) { $hostPart = $hostPart.Substring(0, $slash) }
    if ($hostPart -match '^\d{1,3}(\.\d{1,3}){3},\d+$') { $hostPart = $hostPart.Split(',')[0] }
    elseif ($hostPart -match '^(localhost|[a-z0-9._-]+)(,\d+)?$') { $hostPart = ($hostPart -split ',')[0] }
    return $hostPart
}

$resolvedHost = Resolve-SqlHost $csb.DataSource
$allowedHosts = @('localhost', '127.0.0.1', '::1', '.', '(local)', 'te-sql')
if ($csb.DataSource -match 'database\.windows\.net' -or $allowedHosts -notcontains $resolvedHost) {
    throw "Refusing to run: -LocalDb resolves to data source '$($csb.DataSource)' (host '$resolvedHost'), which is not a recognised local test instance."
}
Write-Host "  -- local guard ok: resolved host '$resolvedHost', database '$($csb.InitialCatalog)'"

$LocalDb = $csb.ConnectionString

function Invoke-LocalSql {
    param([string]$CommandText, [hashtable]$Parameters = @{}, [switch]$NonQuery)
    $cs = [System.Data.SqlClient.SqlConnection]::new($LocalDb)
    try {
        $cs.Open()
        $cmd = $cs.CreateCommand()
        $cmd.CommandText = $CommandText
        foreach ($name in $Parameters.Keys) {
            [void]$cmd.Parameters.AddWithValue($name, $Parameters[$name])
        }
        if ($NonQuery) {
            return $cmd.ExecuteNonQuery()
        }
        return $cmd.ExecuteScalar()
    } finally {
        $cs.Dispose()
    }
}

function Get-LocalLiveIds {
    $cs = [System.Data.SqlClient.SqlConnection]::new($LocalDb)
    try {
        $cs.Open()
        $cmd = $cs.CreateCommand()
        $cmd.CommandText = 'SELECT Id FROM WatchedInstruments WHERE DeletedAt IS NULL'
        $reader = $cmd.ExecuteReader()
        $ids = [System.Collections.Generic.List[string]]::new()
        while ($reader.Read()) {
            $ids.Add($reader.GetGuid(0).ToString().ToLowerInvariant())
        }
        return $ids
    } finally {
        $cs.Dispose()
    }
}

function Get-BaselineSnapshot {
    $cs = [System.Data.SqlClient.SqlConnection]::new($LocalDb)
    try {
        $cs.Open()
        $cmd = $cs.CreateCommand()
        $cmd.CommandText = 'SELECT Id, CONVERT(bigint, RowVersion), DeletedAt FROM WatchedInstruments'
        $reader = $cmd.ExecuteReader()
        $rows = @{}
        while ($reader.Read()) {
            $rows[$reader.GetGuid(0).ToString().ToLowerInvariant()] = '{0}:{1}' -f $reader.GetInt64(1), $(if ($reader.IsDBNull(2)) { 'live' } else { 'deleted' })
        }
        return $rows
    } finally {
        $cs.Dispose()
    }
}

function Get-ApiLiveIds {
    $ids = [System.Collections.Generic.List[string]]::new()
    $pageNumber = 1
    do {
        $pageResult = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/watched-instruments?page=$pageNumber&pageSize=50" -TimeoutSec 30
        foreach ($item in $pageResult.items) { $ids.Add($item.id.ToLowerInvariant()) }
        $pageNumber++
    } while ($ids.Count -lt $pageResult.totalCount -and $pageResult.items.Count -gt 0)
    return $ids
}

function Remove-TestInstrument {
    param([string]$Id)
    $token = $null
    $pageNumber = 1
    do {
        $pageResult = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/watched-instruments?page=$pageNumber&pageSize=50" -TimeoutSec 30
        foreach ($item in $pageResult.items) {
            if ($item.id -eq $Id) {
                $token = $item.concurrencyToken
                break
            }
        }
        $pageNumber++
    } while ($null -eq $token -and ($pageNumber - 1) * 50 -lt $pageResult.totalCount -and $pageResult.items.Count -gt 0)
    if ($null -eq $token) { return 'already-gone' }
    $body = @{ items = @(@{ instrumentId = $Id; concurrencyToken = $token }) } | ConvertTo-Json -Depth 5
    Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/watched-instruments/bulk-delete" `
        -ContentType 'application/json' -Body $body -TimeoutSec 30 | Out-Null
    return 'deleted'
}

# ---------- validate the local database before any mutation ----------

$dbName = [string](Invoke-LocalSql 'SELECT DB_NAME()')
if ($dbName -ne $csb.InitialCatalog) {
    throw "Refusing to run: connected database '$dbName' does not match Initial Catalog '$($csb.InitialCatalog)'."
}
Write-Host "  -- connected to local database '$dbName'"

$baseline = Get-BaselineSnapshot
Write-Host "  -- baseline snapshot: $($baseline.Count) rows"

# ---------- section A: seeding via direct REST/SQL (not browser-driven) ----------

$runTag = [guid]::NewGuid().ToString('N').Substring(0, 8)
$Symbol = "TACPT-$runTag"
$fingerprintId = [Guid]::NewGuid().ToString()
$probeSymbol = 'ZPROBE-' + $fingerprintId.Substring(0, 8)
$instrumentId = $null
$draftId = $null
$scaleTag = "ZP1000-$runTag"
$probeInserted = $false
$scaleInserted = $false

$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

$edgeProc = $null
$script:ws = $null
$profileDir = Join-Path $env:TEMP ('te-acc-' + [guid]::NewGuid().ToString('N'))

try {
    Invoke-LocalSql -NonQuery @'
INSERT INTO WatchedInstruments (Id, Symbol, Exchange, QuoteCurrency, MonitoringState, SamplingIntervalSeconds, CreatedAt, LastChangedAt)
VALUES (@id, @symbol, 'XTEST', 'GBP', 'Configured', 60, @now, @now)
'@ @{ id = [Guid]$fingerprintId; symbol = $probeSymbol; now = [DateTime]::UtcNow } | Out-Null
    $probeInserted = $true

    $apiIds = Get-ApiLiveIds
    $localIds = @(Get-LocalLiveIds | Sort-Object)
    if ((Compare-Object @($apiIds | Sort-Object) $localIds) -or ($apiIds -notcontains $fingerprintId)) {
        throw 'Refusing to run: the API live-instrument set does not match the configured local database - the API may be pointed at a different database.'
    }
    Write-Host "  -- API live-instrument set matches local database ($($localIds.Count) rows incl. probe)"

    Invoke-LocalSql -NonQuery 'UPDATE WatchedInstruments SET DeletedAt = @now WHERE Id = @id' @{
        id  = [Guid]$fingerprintId
        now = [DateTime]::UtcNow
    } | Out-Null
    Write-Host '  -- probe row hidden from live reads'

    $instrumentBody = @{
        symbol                  = $Symbol
        exchange                = 'XTEST'
        quoteCurrency           = 'GBP'
        samplingIntervalSeconds = 60
        monitoringState         = 'configured'
        priceScale              = 4
        supportZones            = @(@{
            id         = 'support-a'
            lower      = 95.0
            level      = 100.0
            upper      = 105.0
            conditions = @(
                @{ type = 'buy-zone'; actionId = 'publish-signal' },
                @{ type = 'support-loss'; actionId = 'publish-signal' }
            )
        })
        resistanceZones         = @(@{
            id         = 'resistance-a'
            lower      = 120.0
            level      = 125.0
            upper      = 130.0
            conditions = @(@{ type = 'breakout'; actionId = 'publish-signal' })
        })
    } | ConvertTo-Json -Depth 8

    $created = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/watched-instruments" `
        -ContentType 'application/json' -Body $instrumentBody -TimeoutSec 30
    $instrumentId = $created.id
    Write-Host "  -- seeded instrument $Symbol id=$instrumentId"

    $found = Invoke-LocalSql 'SELECT COUNT(*) FROM WatchedInstruments WHERE Id = @id' @{ id = [Guid]$instrumentId }
    if ([int]$found -ne 1) {
        try { $strayRemoved = Remove-TestInstrument -Id $instrumentId } catch { $strayRemoved = "failed: $($_.Exception.Message)" }
        throw "Seeded instrument $instrumentId is not present in the configured local database - the API wrote elsewhere. Stray cleanup result: $strayRemoved. Aborting."
    }
    Write-Host '  -- seeded row confirmed in local database'

    $timeline = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/watched-instruments/$instrumentId/monitoring-rule" -TimeoutSec 30
    $ruleToken = $timeline.concurrencyToken

    $draftBody = @{
        changeReason   = 'acceptance seed'
        proposedPeriod = @{
            effectiveFrom = '2027-01-02T03:04:05.1234567Z'
            effectiveTo   = '2027-02-03T04:05:06.1230000Z'
        }
        definition     = @{
            priceScale      = 4
            supportZones    = @(@{
                id         = 'seed-z'
                lower      = 90.0
                level      = 95.0
                upper      = 100.0
                conditions = @(
                    @{ type = 'buy-zone'; actionId = 'act-observe' },
                    @{ type = 'support-loss'; actionId = 'act-observe' }
                )
            })
            resistanceZones = @()
        }
    } | ConvertTo-Json -Depth 8

    $draftsBefore = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/watched-instruments/$instrumentId/monitoring-rule/drafts" -TimeoutSec 30
    Invoke-RestMethod -Method Post `
        -Uri "$BaseUrl/api/watched-instruments/$instrumentId/monitoring-rule/drafts" `
        -ContentType 'application/json' -Headers @{ 'If-Match' = $ruleToken } -Body $draftBody -TimeoutSec 30 | Out-Null
    $draftsAfter = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/watched-instruments/$instrumentId/monitoring-rule/drafts" -TimeoutSec 30
    if ($draftsAfter.drafts.Count -le $draftsBefore.drafts.Count) {
        throw 'Draft creation returned success but no new draft appeared.'
    }
    $draftId = $draftsAfter.drafts[$draftsAfter.drafts.Count - 1].id
    Write-Host "  -- seeded precise draft id=$draftId"

    if ($ScaleRows -gt 0) {
        Invoke-LocalSql -NonQuery @'
BEGIN TRAN;
WITH nums AS (
    SELECT TOP (@count) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM sys.all_columns
)
INSERT INTO WatchedInstruments (Id, Symbol, Exchange, QuoteCurrency, MonitoringState, SamplingIntervalSeconds, CreatedAt, LastChangedAt)
SELECT NEWID(),
       @prefix + RIGHT('0000' + CAST(n AS varchar(4)), 4),
       'XTEST', 'GBP', 'Configured', 60, @now, @now
FROM nums
INSERT INTO WatchedInstruments (Id, Symbol, Exchange, QuoteCurrency, MonitoringState, SamplingIntervalSeconds, CreatedAt, LastChangedAt)
VALUES (NEWID(), @symbol, 'XTEST', 'GBP', 'Configured', 60, @now, @now)
COMMIT TRAN
'@ @{ count = $ScaleRows; prefix = "$scaleTag-"; symbol = "ZZZZLAST-$runTag"; now = [DateTime]::UtcNow } | Out-Null
        $scaleInserted = $true
        Write-Host "  -- seeded $ScaleRows scale rows tagged $scaleTag plus ZZZZLAST-$runTag"
    }

    if ($InjectFault -eq 'scale') {
        throw 'injected fault after scale seeding'
    }

    # ---------- section B: headless Edge via CDP ----------

    if ($null -eq $edge) { throw 'msedge.exe not found' }
    if (Get-NetTCPConnection -LocalPort $DebugPort -State Listen -ErrorAction SilentlyContinue) {
        throw "Port $DebugPort is already in use - a previous headless Edge may still be running."
    }

    $edgeProc = Start-Process -PassThru -FilePath $edge -ArgumentList @(
        '--headless=new', "--remote-debugging-port=$DebugPort", "--user-data-dir=$profileDir",
        '--no-first-run', '--no-default-browser-check', 'about:blank')

    $pageTarget = $null
    foreach ($i in 1..80) {
        try {
            $targets = Invoke-RestMethod -Uri "http://127.0.0.1:$DebugPort/json" -TimeoutSec 2
            $pageTarget = $targets | Where-Object { $_.type -eq 'page' -and $_.url -eq 'about:blank' } | Select-Object -First 1
            if ($null -eq $pageTarget) {
                $pageTarget = $targets | Where-Object { $_.type -eq 'page' } | Select-Object -First 1
            }
            if ($null -ne $pageTarget) { break }
        } catch { }
        Start-Sleep -Milliseconds 250
    }
    if ($null -eq $pageTarget) { throw 'no CDP page target' }

    $script:ws = [System.Net.WebSockets.ClientWebSocket]::new()
    $connectTask = $script:ws.ConnectAsync([Uri]$pageTarget.webSocketDebuggerUrl, [Threading.CancellationToken]::None)
    if (-not $connectTask.Wait(10000)) {
        $script:ws.Abort()
        throw 'CDP connect timed out after 10 s'
    }

    $script:msgId = 0
    function Wait-Task {
        param([Threading.Tasks.Task]$Task, [int]$TimeoutMs, [string]$What)
        if (-not $Task.Wait($TimeoutMs)) {
            try { $script:ws.Abort() } catch { }
            throw "CDP $What timed out after ${TimeoutMs}ms"
        }
        if ($Task.IsFaulted -and $null -ne $Task.Exception) {
            throw $Task.Exception.InnerException
        }
    }

    function Send-Cdp {
        param([string]$Method, [hashtable]$Params = @{})
        $script:msgId++
        $msg = @{ id = $script:msgId; method = $Method; params = $Params } | ConvertTo-Json -Depth 10 -Compress
        $bytes = [Text.Encoding]::UTF8.GetBytes($msg)
        Wait-Task ($script:ws.SendAsync([ArraySegment[byte]]::new($bytes), 'Text', $true, [Threading.CancellationToken]::None)) 15000 "send $Method"
        $accum = ''
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $buf = New-Object byte[] 1048576
            $seg = [ArraySegment[byte]]::new($buf)
            $res = $script:ws.ReceiveAsync($seg, [Threading.CancellationToken]::None)
            $remaining = [int][Math]::Max(1, ($deadline - (Get-Date)).TotalMilliseconds)
            Wait-Task $res $remaining "receive for $Method"
            if ($res.Result.MessageType -eq 'Close' -or $res.Result.Count -eq 0) {
                throw "CDP websocket closed while waiting for response to $Method"
            }
            $accum += [Text.Encoding]::UTF8.GetString($buf, 0, $res.Result.Count)
            if ($res.Result.EndOfMessage) {
                try {
                    $json = $accum | ConvertFrom-Json
                    if ($json.id -eq $script:msgId) { return $json }
                } catch { }
                $accum = ''
            }
        } while ($true)
    }

    function Invoke-Js {
        param([string]$Expression, [bool]$AwaitPromise = $true)
        $r = Send-Cdp -Method 'Runtime.evaluate' -Params @{
            expression    = $Expression
            awaitPromise  = $AwaitPromise
            returnByValue = $true
        }
        if ($r.result.exceptionDetails) {
            throw "JS error: $($r.result.exceptionDetails.text) $($r.result.exceptionDetails.exception.description)"
        }
        return $r.result.result.value
    }

    function Wait-For {
        param([string]$Description, [string]$Expression, [int]$TimeoutSec = 15)
        $deadline = (Get-Date).AddSeconds($TimeoutSec)
        while ((Get-Date) -lt $deadline) {
            try {
                if ((Invoke-Js -Expression $Expression -AwaitPromise $false) -eq $true) { return }
            } catch { }
            Start-Sleep -Milliseconds 200
        }
        throw "timed out waiting for: $Description"
    }

    function Install-FetchTap {
        Invoke-Js @'
            window.__apiCalls = [];
            if (window.__fetchTapped) { 'already'; } else { window.__fetchTapped = true;
            (function(of){ window.fetch = function(u,o){
                const s = (typeof u === 'string') ? u : (u && u.url) || '';
                const m = (o && o.method) || 'GET';
                let body = (o && o.body) || null;
                if (body && body instanceof Uint8Array) { body = new TextDecoder().decode(body); }
                const rec = { method: m, url: s, body: body, status: null };
                window.__apiCalls.push(rec);
                return of.apply(this, arguments).then(function(r){ rec.status = r.status; return r; },
                    function(e){ rec.status = -1; throw e; });
            }; })(window.fetch);
            }
            'patched'
'@ -AwaitPromise $false | Out-Null
    }

    function Set-InputValue {
        param([string]$Selector, [string]$Value)
        Invoke-Js @"
            (function(){
                const el = document.querySelector('$Selector');
                const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
                const setter = Object.getOwnPropertyDescriptor(proto, 'value').set;
                setter.call(el, $(ConvertTo-Json $Value -Compress));
                el.dispatchEvent(new Event('input', { bubbles: true }));
            })();
            'ok'
"@ -AwaitPromise $false | Out-Null
    }

    function Press-Key {
        param([string]$Key, [string]$Code, [int]$WindowsKeyCode)
        Send-Cdp -Method 'Input.dispatchKeyEvent' -Params @{
            type = 'rawKeyDown'; key = $Key; code = $Code; windowsVirtualKeyCode = $WindowsKeyCode;
            nativeVirtualKeyCode = $WindowsKeyCode } | Out-Null
        Send-Cdp -Method 'Input.dispatchKeyEvent' -Params @{
            type = 'keyUp'; key = $Key; code = $Code; windowsVirtualKeyCode = $WindowsKeyCode;
            nativeVirtualKeyCode = $WindowsKeyCode } | Out-Null
    }

    # --- catalogue load and session header ---
    Send-Cdp -Method 'Page.enable' | Out-Null
    Send-Cdp -Method 'Page.navigate' -Params @{ url = "$BaseUrl/" } | Out-Null
    Wait-For 'catalogue rows' "document.querySelectorAll('table[aria-label=`"Watched instruments`"] tbody tr').length > 0" -TimeoutSec 60

    Check 'catalogue rows rendered' (
        (Invoke-Js "document.querySelectorAll('table[aria-label=`"Watched instruments`"] tbody tr').length" -AwaitPromise $false) -gt 0)

    Wait-For 'session header resolved' "((document.querySelector('.te-session')||{innerText:''}).innerText||'').indexOf('Local development') >= 0"
    Check 'session header shows Local development' $true

    Check 'selection status starts at 0' (
        (Invoke-Js "document.querySelector('.te-toolbar .te-small').textContent.trim()" -AwaitPromise $false) -eq '0 selected on this page')

    # --- scale paging and full-collection search ---
    if ($scaleInserted) {
        $liveBaseline = @($baseline.Keys | Where-Object { $baseline[$_].EndsWith(':live') }).Count
        $expectedTotal = $liveBaseline + $ScaleRows + 2
        Wait-For 'scale total' "/Showing [0-9]+[^0-9]+[0-9]+ of [0-9]+ instruments/.test(document.body.innerText)"
        $rangeText = Invoke-Js "(document.querySelector('.te-pagination .te-small')||{textContent:''}).textContent.trim()" -AwaitPromise $false
        Write-Host "  -- range text: $rangeText"
        $rangeDigits = ($rangeText -replace '\D', ' ')
        Check 'scale total shown' ($rangeDigits -match "^\s*1\s+25\s+$expectedTotal\s*$")

        $pageLabel = Invoke-Js "(document.querySelector('.te-page-label')||{textContent:''}).textContent.trim()" -AwaitPromise $false
        $totalPages = [int][Math]::Ceiling($expectedTotal / 25)
        Check 'page count shown' ($pageLabel -eq "Page 1 of $totalPages")

        Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === 'Last').click(); 'ok'" -AwaitPromise $false | Out-Null
        Wait-For 'last page' "(document.querySelector('.te-page-label')||{textContent:''}).textContent.trim() === 'Page $totalPages of $totalPages'"
        Check 'last page navigable' $true

        Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === 'First').click(); 'ok'" -AwaitPromise $false | Out-Null
        Wait-For 'first page' "(document.querySelector('.te-page-label')||{textContent:''}).textContent.trim() === 'Page 1 of $totalPages'"

        Install-FetchTap
        Set-InputValue "input[type=search]" "ZZZZLAST-$runTag"
        Wait-For 'search request completed' "window.__apiCalls.filter(c => c.method === 'GET' && c.url.indexOf('q=') >= 0 && c.status !== null).length > 0" -TimeoutSec 20
        Wait-For 'single filtered match' "document.querySelectorAll('tbody tr').length === 1"
        Check 'search finds match beyond first page' (
            (Invoke-Js "(document.querySelector('tbody .te-open')||{textContent:''}).textContent.trim()" -AwaitPromise $false) -eq "ZZZZLAST-$runTag" -and
            (Invoke-Js "location.href.indexOf('q=') >= 0" -AwaitPromise $false) -eq $true)

        Invoke-Js "history.back(); 'ok'" -AwaitPromise $false | Out-Null
        Wait-For 'back restores prior URL' "location.href.indexOf('q=') < 0 && location.href.indexOf('page=$totalPages') >= 0" -TimeoutSec 15
        Wait-For 'back restores prior page' "(document.querySelector('.te-page-label')||{textContent:''}).textContent.trim() === 'Page $totalPages of $totalPages'" -TimeoutSec 15
        Check 'browser back restores list state and URL' $true

        Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === 'First').click(); 'ok'" -AwaitPromise $false | Out-Null
        Wait-For 'back to first page' "(document.querySelector('.te-page-label')||{textContent:''}).textContent.trim() === 'Page 1 of $totalPages'" -TimeoutSec 15
    }

    # --- selection cleared before debounced request; result set must change ---
    $rowsBefore = Invoke-Js "Array.from(document.querySelectorAll('tbody [data-record]')).map(e => e.getAttribute('data-record')).join(',')" -AwaitPromise $false

    Invoke-Js "document.querySelector('table tbody input[type=checkbox]').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'one row selected' "document.querySelector('.te-toolbar .te-small').textContent.trim() === '1 selected on this page'"
    Check 'one row selects' $true

    Install-FetchTap
    Set-InputValue "input[type=search]" $Symbol

    $cleared = Invoke-Js @'
        new Promise(res => {
            const t0 = Date.now();
            const timer = setInterval(() => {
                const st = (document.querySelector('.te-toolbar .te-small')||{textContent:''}).textContent.trim();
                const gets = window.__apiCalls.filter(c => c.method === 'GET' && c.url.indexOf('/watched-instruments') >= 0).length;
                if (st === '0 selected on this page' || Date.now() - t0 > 250) {
                    clearInterval(timer);
                    res({ cleared: st === '0 selected on this page', gets: gets });
                }
            }, 20);
        })
'@
    Check 'selection cleared before search request' ($cleared.cleared -eq $true -and $cleared.gets -eq 0)

    Wait-For 'search response completed' "window.__apiCalls.filter(c => c.method === 'GET' && c.url.indexOf('q=') >= 0 && c.status !== null).length > 0" -TimeoutSec 20
    Wait-For 'result set changed' "Array.from(document.querySelectorAll('tbody [data-record]')).map(e => e.getAttribute('data-record')).join(',') !== '$rowsBefore'"
    Check 'filtered results contain only the run row' (
        (Invoke-Js "Array.from(document.querySelectorAll('tbody [data-record]')).map(e => e.getAttribute('data-record')).join(',')" -AwaitPromise $false) -eq $instrumentId.ToLowerInvariant())

    # --- selection is locked while a new search is pending ---
    Invoke-Js "document.querySelector('table tbody input[type=checkbox]').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'row re-selected' "document.querySelector('.te-toolbar .te-small').textContent.trim() === '1 selected on this page'"
    Set-InputValue "input[type=search]" "$Symbol-x"
    Wait-For 'pending lock disables rows' "document.querySelectorAll('tbody input[type=checkbox]').length > 0 && Array.from(document.querySelectorAll('tbody input[type=checkbox]')).every(c => c.disabled)" -TimeoutSec 5
    Check 'row checkboxes disabled during pending search' $true
    Check 'selection cleared on new input' (
        (Invoke-Js "document.querySelector('.te-toolbar .te-small').textContent.trim()" -AwaitPromise $false) -eq '0 selected on this page')
    Wait-For 'no-match result' "document.body.innerText.indexOf('No instruments found') >= 0" -TimeoutSec 20
    Check 'search with no match shows empty state' $true

    Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === 'Clear search').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'cleared list' "document.querySelectorAll('tbody tr').length > 0" -TimeoutSec 20

    # --- confirmation panel and cancel ---
    $picked = Invoke-Js @"
        (function(){
            const btn = document.querySelector('tbody [data-record="$instrumentId"]');
            if (!btn) { return 'missing'; }
            btn.closest('tr').querySelector('input[type=checkbox]').click();
            return 'ok';
        })()
"@ -AwaitPromise $false
    if ($picked -ne 'ok') { throw "test-owned row $Symbol ($instrumentId) not found on the catalogue page" }
    Wait-For 'row selected for confirm' "document.querySelector('.te-toolbar .te-small').textContent.trim() === '1 selected on this page'"
    Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === 'Delete selected').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'confirmation panel' "!!document.querySelector('.te-confirm-list')"
    Check 'confirmation panel lists selection' (
        (Invoke-Js "(document.querySelector('.te-confirm-list li')||{textContent:''}).textContent" -AwaitPromise $false) -match [regex]::Escape($Symbol) -and
        (Invoke-Js "document.body.innerText.indexOf('Revision and audit history') >= 0" -AwaitPromise $false) -eq $true)

    Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === 'Cancel').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'panel closed' "!document.querySelector('.te-confirm-list')"
    Check 'cancel closes panel keeps selection' (
        (Invoke-Js "document.querySelector('.te-toolbar .te-small').textContent.trim()" -AwaitPromise $false) -eq '1 selected on this page')

    # --- keyboard navigation ---
    Invoke-Js "document.querySelector('input[type=search]').focus(); 'ok'" -AwaitPromise $false | Out-Null
    Press-Key 'Tab' 'Tab' 9
    $tabStops = @()
    foreach ($i in 1..6) {
        $focus = Invoke-Js "(document.activeElement ? (document.activeElement.getAttribute('aria-label') || document.activeElement.textContent.trim() || document.activeElement.tagName) : 'none')" -AwaitPromise $false
        $tabStops += $focus
        Press-Key 'Tab' 'Tab' 9
    }
    Write-Host "  -- tab order: $($tabStops -join ' -> ')"
    $distinctStops = @($tabStops | Select-Object -Unique)
    Check 'keyboard tab moves focus through controls' (
        $distinctStops.Count -ge 3 -and
        ($distinctStops | Where-Object { $_ -match 'Select|Delete' }).Count -ge 2)

    # --- instrument detail: tabs and drafts ---
    $symbolUpper = $Symbol.ToUpperInvariant()
    Send-Cdp -Method 'Page.navigate' -Params @{ url = "$BaseUrl/instruments/$instrumentId" } | Out-Null
    Wait-For 'instrument detail' "document.body.innerText.indexOf('$symbolUpper') >= 0" -TimeoutSec 20
    Check 'detail tabs render' (
        (Invoke-Js "Array.from(document.querySelectorAll('.te-tabs button')).map(b => b.textContent.trim()).join(',')" -AwaitPromise $false) -match 'Current|Settings|Drafts|Timeline')

    Invoke-Js "Array.from(document.querySelectorAll('.te-tabs button')).find(b => b.textContent.trim().indexOf('Drafts') === 0).click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'drafts list' "document.body.innerText.indexOf('acceptance seed') >= 0" -TimeoutSec 15
    Check 'drafts view lists seeded draft' $true

    Invoke-Js "Array.from(document.querySelectorAll('.te-tabs button')).find(b => b.textContent.trim().indexOf('Timeline') === 0).click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'timeline view' "(Array.from(document.querySelectorAll('.te-tabs button')).find(b => b.textContent.trim().indexOf('Timeline') === 0)||{getAttribute:function(){return null}}).getAttribute('aria-pressed') !== null" -TimeoutSec 10
    Check 'timeline view renders' (
        (Invoke-Js "document.body.innerText.indexOf('Revision 1') >= 0" -AwaitPromise $false) -eq $true)

    # --- settings conflict: REST write bumps the token, browser save must surface conflict ---
    Invoke-Js "Array.from(document.querySelectorAll('.te-tabs button')).find(b => b.textContent.trim() === 'Settings').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'settings editor' "!!document.querySelector('input[aria-label=`"Sampling interval seconds`"]')" -TimeoutSec 15

    $settings = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/watched-instruments/$instrumentId/settings" -TimeoutSec 30
    $bump = @{ monitoringState = 'configured'; samplingIntervalSeconds = 120 } | ConvertTo-Json
    Invoke-RestMethod -Method Put -Uri "$BaseUrl/api/watched-instruments/$instrumentId/settings" `
        -ContentType 'application/json' -Headers @{ 'If-Match' = $settings.concurrencyToken } -Body $bump -TimeoutSec 30 | Out-Null
    Write-Host '  -- REST: bumped sampling interval to 120 (fresh token consumed)'

    Set-InputValue "input[aria-label=`"Sampling interval seconds`"]" '90'
    Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === 'Save settings').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'settings conflict notice' "document.body.innerText.indexOf('Server now shows') >= 0" -TimeoutSec 20
    Check 'stale settings save shows conflict' $true
    Check 'conflict keeps user input' (
        (Invoke-Js "document.body.innerText.indexOf('Your input:') >= 0 && document.querySelector('input[aria-label=`"Sampling interval seconds`"]').value === '90'" -AwaitPromise $false) -eq $true)

    Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim().indexOf('Use latest') === 0).click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'conflict discarded' "document.body.innerText.indexOf('Server now shows') < 0" -TimeoutSec 15
    Check 'discard loads latest settings' (
        (Invoke-Js "document.querySelector('input[aria-label=`"Sampling interval seconds`"]').value" -AwaitPromise $false) -eq '120')
    Invoke-RestMethod -Method Put -Uri "$BaseUrl/api/watched-instruments/$instrumentId/settings" `
        -ContentType 'application/json' -Headers @{ 'If-Match' = (Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/watched-instruments/$instrumentId/settings" -TimeoutSec 30).concurrencyToken } `
        -Body (@{ monitoringState = 'configured'; samplingIntervalSeconds = 60 } | ConvertTo-Json) -TimeoutSec 30 | Out-Null

    # --- draft editor: exact values, precision, save round-trip ---
    Send-Cdp -Method 'Page.navigate' -Params @{ url = "$BaseUrl/instruments/$instrumentId/drafts/$draftId" } | Out-Null
    Wait-For 'datetime-local controls' "document.querySelectorAll('input[type=`"datetime-local`"]').length > 0" -TimeoutSec 25

    $fromValue = Invoke-Js "(document.querySelector('input[aria-label=`"Proposed effective from UTC`"]')||{value:''}).value" -AwaitPromise $false
    $toValue = Invoke-Js "(document.querySelector('input[aria-label=`"Proposed effective to UTC`"]')||{value:''}).value" -AwaitPromise $false
    Write-Host "  -- datetime values: from='$fromValue' to='$toValue'"
    Check 'effective-from control has exact ms value' ($fromValue -eq '2027-01-02T03:04:05.123')
    Check 'effective-to control has exact ms value' ($toValue -eq '2027-02-03T04:05:06.123')

    Check 'step permits millisecond input' (
        (Invoke-Js "Array.from(document.querySelectorAll('input[type=`"datetime-local`"]')).every(i => i.step === '0.001')" -AwaitPromise $false) -eq $true)

    Check 'precise baseline note shown' (
        (Invoke-Js "document.body.innerText.indexOf('1234567') >= 0" -AwaitPromise $false) -eq $true)

    $sanitized = Invoke-Js @'
        (function(){
            const i = document.querySelector('input[type="datetime-local"]');
            i.value = '2027-01-02T03:04:05.1234567';
            const v = i.value;
            i.value = '2027-01-02T03:04:05.123';
            return v;
        })()
'@ -AwaitPromise $false
    Check 'browser rejects 7 fractional digits' ($sanitized -eq '')

    Install-FetchTap
    Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === 'Save draft').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'draft save PUT succeeded' "window.__apiCalls.filter(c => c.method === 'PUT' && c.url.indexOf('/monitoring-rule/drafts') >= 0 && c.status >= 200 && c.status < 300).length > 0" -TimeoutSec 20
    $saveBody = Invoke-Js "window.__apiCalls.find(c => c.method === 'PUT' && c.url.indexOf('/monitoring-rule/drafts') >= 0).body" -AwaitPromise $false
    Check 'untouched sub-millisecond baseline preserved in request' (
        $saveBody -match '1234567Z' -and $saveBody -match '1230000Z')

    Send-Cdp -Method 'Page.navigate' -Params @{ url = "$BaseUrl/instruments/$instrumentId/drafts/$draftId" } | Out-Null
    Wait-For 'datetime controls after reload' "document.querySelectorAll('input[type=`"datetime-local`"]').length > 0" -TimeoutSec 25
    $fromAfter = Invoke-Js "(document.querySelector('input[aria-label=`"Proposed effective from UTC`"]')||{value:''}).value" -AwaitPromise $false
    Check 'reloaded control still shows ms value' ($fromAfter -eq '2027-01-02T03:04:05.123')

    $persisted = Invoke-Js "fetch('/api/watched-instruments/$instrumentId/monitoring-rule/drafts').then(r => r.json()).then(d => JSON.stringify(d))" -AwaitPromise $true
    Check 'seven-digit timestamps persisted on server' (
        $persisted -match '05\.1234567' -and $persisted -match '06\.123')

    # --- deletion of the test-owned row, verified by GUID ---
    Send-Cdp -Method 'Page.navigate' -Params @{ url = "$BaseUrl/" } | Out-Null
    Wait-For 'catalogue after back-nav' "document.querySelectorAll('table[aria-label=`"Watched instruments`"] tbody tr').length > 0" -TimeoutSec 20
    Install-FetchTap

    Set-InputValue "input[type=search]" $Symbol
    Wait-For 'own row filtered' "document.querySelectorAll('tbody tr').length === 1 && !!document.querySelector('[data-record=`"$instrumentId`"]')" -TimeoutSec 20

    Invoke-Js "document.querySelector('[data-record=`"$instrumentId`"]').closest('tr').querySelector('input[type=checkbox]').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'selected for delete' "document.querySelector('.te-toolbar .te-small').textContent.trim() === '1 selected on this page'"
    Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === 'Delete selected').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'confirm shown' "!!document.querySelector('.te-confirm-list')"
    Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim().indexOf('Delete 1 instrument') === 0).click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'delete POST succeeded' "window.__apiCalls.filter(c => c.method === 'POST' && c.url.indexOf('bulk-delete') >= 0 && c.status >= 200 && c.status < 300).length > 0" -TimeoutSec 20
    Wait-For 'owned row gone' "document.querySelectorAll('[data-record=`"$instrumentId`"]').length === 0" -TimeoutSec 20
    Check 'test-owned row deleted by GUID' $true

    $gone = $false
    try {
        Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/watched-instruments/$instrumentId" -TimeoutSec 30 | Out-Null
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -eq 404) { $gone = $true }
    }
    Check 'API no longer returns the deleted instrument' $gone
}
finally {
    if ($null -ne $instrumentId) {
        try {
            $cleanupResult = Remove-TestInstrument -Id $instrumentId
            Write-Host "  -- cleanup: instrument $instrumentId -> $cleanupResult"
        } catch {
            $script:cleanupFailures.Add("instrument $instrumentId : $($_.Exception.Message)")
            Write-Host "  -- cleanup FAILED for instrument $instrumentId : $($_.Exception.Message)"
        }
    }
    if ($probeInserted) {
        try {
            Invoke-LocalSql -NonQuery 'DELETE FROM WatchedInstruments WHERE Id = @id' @{ id = [Guid]$fingerprintId } | Out-Null
            Write-Host '  -- cleanup: removed probe row'
        } catch {
            $script:cleanupFailures.Add("probe $fingerprintId : $($_.Exception.Message)")
            Write-Host "  -- cleanup FAILED for probe: $($_.Exception.Message)"
        }
    }
    try {
        $removed = Invoke-LocalSql -NonQuery 'DELETE FROM WatchedInstruments WHERE Symbol LIKE @p1 OR Symbol LIKE @p2' @{
            p1 = "$scaleTag-%"; p2 = "ZZZZLAST-$runTag"
        }
        Write-Host "  -- cleanup: removed $removed scale rows"
    } catch {
        $script:cleanupFailures.Add("scale rows $scaleTag : $($_.Exception.Message)")
        Write-Host "  -- cleanup FAILED for scale rows: $($_.Exception.Message)"
    }
    try { if ($null -ne $script:ws) { $script:ws.Dispose() } } catch { }
    try { if ($null -ne $edgeProc) { Stop-Process -Id $edgeProc.Id -Force -ErrorAction SilentlyContinue } } catch { }
    try {
        Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" |
            Where-Object { $_.CommandLine -like "*$profileDir*" } |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    } catch { }
    try { Remove-Item -Recurse -Force $profileDir -ErrorAction SilentlyContinue } catch { }

    try {
        $after = Get-BaselineSnapshot
        $drifted = @()
        foreach ($id in $baseline.Keys) {
            if (-not $after.ContainsKey($id) -or $after[$id] -ne $baseline[$id]) {
                $drifted += $id
            }
        }
        $leftovers = @($after.Keys | Where-Object { $after[$_] -like '*:live' -and -not $baseline.ContainsKey($_) })
        if ($drifted.Count -gt 0) {
            Check 'unrelated records unchanged' $false
            Write-Host "  -- CHANGED baseline rows: $($drifted -join ', ')"
        } elseif ($leftovers.Count -gt 0) {
            Check 'unrelated records unchanged' $false
            Write-Host "  -- residual live rows created this run: $($leftovers -join ', ')"
        } else {
            Check 'unrelated records unchanged' $true
        }
    } catch {
        Write-Host "  -- post-run verification failed: $($_.Exception.Message)"
        Check 'unrelated records unchanged' $false
    }

    Write-Host ''
    Write-Host '=== SUMMARY ==='
    $script:results | Format-Table -AutoSize | Out-Host
    $failed = @($script:results | Where-Object { $_.Result -eq 'FAIL' }).Count
    Write-Host "$($script:results.Count - $failed)/$($script:results.Count) checks passed"
    if ($script:cleanupFailures.Count -gt 0) {
        Write-Host "CLEANUP FAILURES: $($script:cleanupFailures -join '; ')"
    }
}

if ($script:cleanupFailures.Count -gt 0) {
    exit 200
}
exit @($script:results | Where-Object { $_.Result -eq 'FAIL' }).Count
