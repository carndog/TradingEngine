#Requires -Version 5.1
<#
.SYNOPSIS
  Local browser acceptance harness for the watched-instrument catalogue.

  Seeds synthetic data through the REST API (section A), then drives headless
  Edge through the Chrome DevTools Protocol to exercise the rendered UI
  (section B). Synthetic data only; intended for the local API on :5068 with
  the disposable te-sql database. Direct REST calls in section A are seeding
  and cleanup, not browser-driven assertions.

  REQUIRES -LocalDb or $env:TE_ACCEPTANCE_DB set to the disposable local SQL
  connection string (for example the te-sql container). The harness aborts
  unless the configured database is a local instance and the API's live
  instrument set matches it exactly, so a misconfigured API cannot write to a
  shared database.
#>
param(
    [string]$BaseUrl = 'http://localhost:5068',
    [string]$Symbol = 'TACPT',
    [string]$LocalDb = $env:TE_ACCEPTANCE_DB
)

$ErrorActionPreference = 'Stop'
$script:results = [System.Collections.Generic.List[object]]::new()

function Check {
    param([string]$Name, [bool]$Ok)
    $script:results.Add([pscustomobject]@{ Check = $Name; Result = $(if ($Ok) { 'PASS' } else { 'FAIL' }) })
    Write-Host "$(if ($Ok) { 'PASS' } else { 'FAIL' }) $Name :: $Ok"
}

# ---------- section A: seeding via direct REST (not browser-driven) ----------

if ([string]::IsNullOrWhiteSpace($LocalDb)) {
    throw 'Refusing to run: set -LocalDb or $env:TE_ACCEPTANCE_DB to the disposable local SQL connection string.'
}
$serverValue = ([regex]::Match($LocalDb, '(?i)(?:server|data source)\s*=\s*([^;]+)')).Groups[1].Value.Trim()
$serverHost = ($serverValue -split '[,:]')[0].Trim().ToLowerInvariant()
$allowedHosts = @('localhost', '127.0.0.1', '::1', '.', '(local)', 'te-sql')
if ($LocalDb -match 'database\.windows\.net' -or $allowedHosts -notcontains $serverHost) {
    throw "Refusing to run: -LocalDb server '$serverValue' is not a recognised local test instance."
}

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

function Remove-TestInstrument {
    param([string]$Id)
    $live = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/watched-instruments?page=1&pageSize=50"
    $mine = $live.items | Where-Object { $_.id -eq $Id } | Select-Object -First 1
    if ($null -eq $mine) { return $false }
    $body = @{ items = @(@{ instrumentId = $Id; concurrencyToken = $mine.concurrencyToken }) } | ConvertTo-Json -Depth 5
    Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/watched-instruments/bulk-delete" `
        -ContentType 'application/json' -Body $body | Out-Null
    return $true
}

$dbName = Invoke-LocalSql 'SELECT DB_NAME()'
Write-Host "  -- local test database verified: $dbName"

$fingerprintId = [Guid]::NewGuid().ToString()
Invoke-LocalSql -NonQuery @'
INSERT INTO WatchedInstruments (Id, Symbol, Exchange, QuoteCurrency, MonitoringState, SamplingIntervalSeconds, CreatedAt, LastChangedAt)
VALUES (@id, @symbol, 'XTEST', 'GBP', 'Configured', 60, @now, @now)
'@ @{ id = [Guid]$fingerprintId; symbol = 'ZPROBE-' + $fingerprintId.Substring(0, 8); now = [DateTime]::UtcNow } | Out-Null

$apiIds = [System.Collections.Generic.List[string]]::new()
$pageNumber = 1
do {
    $pageResult = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/watched-instruments?page=$pageNumber&pageSize=50"
    foreach ($item in $pageResult.items) { $apiIds.Add($item.id.ToLowerInvariant()) }
    $pageNumber++
} while ($apiIds.Count -lt $pageResult.totalCount -and $pageResult.items.Count -gt 0)

$localIds = @(Get-LocalLiveIds | Sort-Object)
$apiIdSet = @($apiIds | Sort-Object)
if ((Compare-Object $apiIdSet $localIds) -or ($apiIds -notcontains $fingerprintId)) {
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

$instrumentId = $null
try {
    $created = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/watched-instruments" `
        -ContentType 'application/json' -Body $instrumentBody
    $instrumentId = $created.id
    Write-Host "  -- seeded instrument $Symbol id=$instrumentId"
} catch {
    $page = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/watched-instruments?q=$Symbol&page=1&pageSize=10"
    $match = $page.items | Where-Object { $_.symbol -eq $Symbol } | Select-Object -First 1
    if ($null -eq $match) { throw "registration failed and no existing $Symbol row found: $_" }
    $instrumentId = $match.id
    Write-Host "  -- reusing existing instrument $Symbol id=$instrumentId"
}

$found = Invoke-LocalSql 'SELECT COUNT(*) FROM WatchedInstruments WHERE Id = @id' @{ id = [Guid]$instrumentId }
if ([int]$found -ne 1) {
    try { $strayRemoved = Remove-TestInstrument -Id $instrumentId } catch { $strayRemoved = $false }
    throw "Seeded instrument $instrumentId is not present in the configured local database - the API wrote elsewhere. Stray row removed via API: $strayRemoved. Aborting."
}
Write-Host "  -- seeded row confirmed in local database"

$timeline = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/watched-instruments/$instrumentId/monitoring-rule"
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

$drafts = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/watched-instruments/$instrumentId/monitoring-rule/drafts"
$draftId = $null
try {
    Invoke-RestMethod -Method Post `
        -Uri "$BaseUrl/api/watched-instruments/$instrumentId/monitoring-rule/drafts" `
        -ContentType 'application/json' -Headers @{ 'If-Match' = $ruleToken } -Body $draftBody | Out-Null
    $drafts = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/watched-instruments/$instrumentId/monitoring-rule/drafts"
    $draftId = $drafts.drafts[$drafts.drafts.Count - 1].id
    Write-Host "  -- seeded precise draft id=$draftId"
} catch {
    if ($drafts.drafts.Count -eq 0) { throw }
    $draftId = $drafts.drafts[0].id
    Write-Host "  -- draft creation failed; reusing existing draft id=$draftId"
}

# ---------- section B: headless Edge via CDP ----------

$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($null -eq $edge) { throw 'msedge.exe not found' }

$profileDir = Join-Path $env:TEMP ('te-acc-' + [guid]::NewGuid().ToString('N'))
if (Get-NetTCPConnection -LocalPort 9333 -State Listen -ErrorAction SilentlyContinue) {
    throw 'Port 9333 is already in use - a previous headless Edge may still be running.'
}
$edgeProc = $null
$ws = $null
try {
$edgeProc = Start-Process -PassThru -FilePath $edge -ArgumentList @(
    '--headless=new', '--remote-debugging-port=9333', "--user-data-dir=$profileDir",
    '--no-first-run', '--no-default-browser-check', 'about:blank')

$pageTarget = $null
foreach ($i in 1..80) {
    try {
        $targets = Invoke-RestMethod -Uri 'http://127.0.0.1:9333/json' -TimeoutSec 2
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
$script:ws.ConnectAsync([Uri]$pageTarget.webSocketDebuggerUrl, [Threading.CancellationToken]::None).Wait()

$script:msgId = 0
function Send-Cdp {
    param([string]$Method, [hashtable]$Params = @{})
    $script:msgId++
    $msg = @{ id = $script:msgId; method = $Method; params = $Params } | ConvertTo-Json -Depth 10 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($msg)
    $script:ws.SendAsync([ArraySegment[byte]]::new($bytes), 'Text', $true, [Threading.CancellationToken]::None).Wait()
    $accum = ''
    $receives = 0
    do {
        $buf = New-Object byte[] 1048576
        $seg = [ArraySegment[byte]]::new($buf)
        $res = $script:ws.ReceiveAsync($seg, [Threading.CancellationToken]::None)
        $res.Wait()
        if ($res.Result.MessageType -eq 'Close' -or $res.Result.Count -eq 0) {
            throw "CDP websocket closed while waiting for response to $Method"
        }
        if (++$receives -gt 2000) {
            throw "No CDP response to $Method after 2000 frames"
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

    Send-Cdp -Method 'Page.enable' | Out-Null
    Send-Cdp -Method 'Page.navigate' -Params @{ url = "$BaseUrl/" } | Out-Null
    Wait-For 'catalogue rows' "document.querySelectorAll('table[aria-label=`"Watched instruments`"] tbody tr').length > 0"

    Check 'catalogue rows rendered' (
        (Invoke-Js "document.querySelectorAll('table[aria-label=`"Watched instruments`"] tbody tr').length" -AwaitPromise $false) -gt 0)

    Wait-For 'session header resolved' "((document.querySelector('.te-session')||{innerText:''}).innerText||'').indexOf('Local development') >= 0"
    Check 'session header shows Local development' $true

    Check 'selection status starts at 0' (
        (Invoke-Js "document.querySelector('.te-toolbar .te-small').textContent.trim()" -AwaitPromise $false) -eq '0 selected on this page')

    Invoke-Js "document.querySelector('table tbody input[type=checkbox]').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'one row selected' "document.querySelector('.te-toolbar .te-small').textContent.trim() === '1 selected on this page'"
    Check 'one row selects' $true

    Invoke-Js @"
        window.__apiCalls = [];
        (function(of){ window.fetch = function(u,o){
            try {
                const s = (typeof u === 'string') ? u : (u && u.url) || '';
                const m = (o && o.method) || 'GET';
                window.__apiCalls.push({ method: m, url: s,
                    sel: (document.querySelector('.te-toolbar .te-small')||{}).textContent });
            } catch(e){}
            return of.apply(this, arguments);
        }; })(window.fetch);
        'patched'
"@ -AwaitPromise $false | Out-Null

    $searchSymbol = Invoke-Js "document.querySelector('tbody .te-open').textContent.trim()" -AwaitPromise $false
    Write-Host "  -- searching for symbol: $searchSymbol"

    Invoke-Js @"
        (function(){
            const s = document.querySelector('input[type=search]');
            const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
            setter.call(s, '$searchSymbol');
            s.dispatchEvent(new Event('input', { bubbles: true }));
        })();
        'typed'
"@ -AwaitPromise $false | Out-Null

    $cleared = Invoke-Js @"
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
"@
    Check 'selection cleared before search request' ($cleared.cleared -eq $true -and $cleared.gets -eq 0)

    Wait-For 'search request issued' "window.__apiCalls.filter(c => c.method === 'GET' && c.url.indexOf('/watched-instruments') >= 0 && c.url.indexOf('q=') >= 0).length > 0"
    Write-Host '  -- search request issued after debounce'
    Wait-For 'filtered rows' "document.querySelector('tbody .te-open') && document.querySelector('tbody .te-open').textContent.trim().indexOf('$searchSymbol') >= 0"
    Check 'filtered results rendered' $true

    Invoke-Js "document.querySelector('table tbody input[type=checkbox]').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'row re-selected' "document.querySelector('.te-toolbar .te-small').textContent.trim() === '1 selected on this page'"
    Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === 'Delete selected').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'confirmation panel' "!!document.querySelector('.te-confirm-list')"
    Check 'confirmation panel lists selection' (
        (Invoke-Js "(document.querySelector('.te-confirm-list li')||{textContent:''}).textContent" -AwaitPromise $false) -match [regex]::Escape($searchSymbol) -and
        (Invoke-Js "document.body.innerText.indexOf('Revision and audit history') >= 0" -AwaitPromise $false) -eq $true)

    Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === 'Cancel').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'panel closed' "!document.querySelector('.te-confirm-list')"
    Check 'cancel closes panel keeps selection' (
        (Invoke-Js "document.querySelector('.te-toolbar .te-small').textContent.trim()" -AwaitPromise $false) -eq '1 selected on this page')

    # datetime-local precision on the seeded draft editor
    Send-Cdp -Method 'Page.navigate' -Params @{ url = "$BaseUrl/instruments/$instrumentId/drafts/$draftId" } | Out-Null
    Wait-For 'datetime-local controls' "document.querySelectorAll('input[type=`"datetime-local`"]').length > 0" -TimeoutSec 20

    $dtValues = Invoke-Js "Array.from(document.querySelectorAll('input[type=`"datetime-local`"]')).map(i => i.value).join(' | ')" -AwaitPromise $false
    Write-Host "  -- datetime-local values: $dtValues"
    Check 'control value is millisecond precision' (
        (Invoke-Js "Array.from(document.querySelectorAll('input[type=`"datetime-local`"]')).filter(i => i.value !== '').every(i => /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}$/.test(i.value))" -AwaitPromise $false) -eq $true)

    Check 'step permits millisecond input' (
        (Invoke-Js "Array.from(document.querySelectorAll('input[type=`"datetime-local`"]')).every(i => i.step === '0.001')" -AwaitPromise $false) -eq $true)

    Check 'precise baseline note shown' (
        (Invoke-Js "document.body.innerText.indexOf('1234567') >= 0" -AwaitPromise $false) -eq $true)

    $sanitized = Invoke-Js @"
        (function(){
            const i = document.querySelector('input[type="datetime-local"]');
            i.value = '2027-01-02T03:04:05.1234567';
            const v = i.value;
            i.value = '2027-01-02T03:04:05.123';
            return v;
        })()
"@ -AwaitPromise $false
    Write-Host "  -- browser sanitized 7-digit value to: '$sanitized'"
    Check 'browser rejects 7 fractional digits' ($sanitized -eq '')

    Invoke-Js @"
        window.__reqBodies = [];
        (function(of){ window.fetch = function(u,o){
            try {
                const s = (typeof u === 'string') ? u : (u && u.url) || '';
                const m = (o && o.method) || 'GET';
                let body = (o && o.body) || null;
                if (body && body instanceof Uint8Array) { body = new TextDecoder().decode(body); }
                window.__reqBodies.push({ method: m, url: s, body: body });
            } catch(e){}
            return of.apply(this, arguments);
        }; })(window.fetch);
        'patched'
"@ -AwaitPromise $false | Out-Null

    Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === 'Save draft').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'draft save PUT' "window.__reqBodies.filter(c => c.method === 'PUT' && c.url.indexOf('/monitoring-rule/drafts') >= 0).length > 0" -TimeoutSec 20
    $saveBody = Invoke-Js "window.__reqBodies.find(c => c.method === 'PUT' && c.url.indexOf('/monitoring-rule/drafts') >= 0).body" -AwaitPromise $false
    Write-Host "  -- save request body: $($saveBody.Substring(0, [Math]::Min(220, $saveBody.Length)))"
    Check 'untouched sub-millisecond baseline preserved in request' (
        $saveBody -match '1234567Z' -and $saveBody -match '1230000Z')

    # bulk delete on the catalogue
    Send-Cdp -Method 'Page.navigate' -Params @{ url = "$BaseUrl/" } | Out-Null
    Wait-For 'catalogue rows after back-nav' "document.querySelectorAll('table[aria-label=`"Watched instruments`"] tbody tr').length > 0" -TimeoutSec 20
    $rowsBefore = Invoke-Js "document.querySelectorAll('table[aria-label=`"Watched instruments`"] tbody tr').length" -AwaitPromise $false
    Write-Host "  -- rows before delete: $rowsBefore"

    Invoke-Js @"
        window.__apiCalls = [];
        (function(of){ window.fetch = function(u,o){
            try {
                const s = (typeof u === 'string') ? u : (u && u.url) || '';
                window.__apiCalls.push({ method: (o && o.method) || 'GET', url: s });
            } catch(e){}
            return of.apply(this, arguments);
        }; })(window.fetch);
        'patched'
"@ -AwaitPromise $false | Out-Null

    $picked = Invoke-Js @"
        (function(){
            const row = Array.from(document.querySelectorAll('tbody tr'))
                .find(tr => ((tr.querySelector('.te-open')||{}).textContent||'').trim() === '$Symbol');
            if (!row) { return 'missing'; }
            row.querySelector('input[type=checkbox]').click();
            return 'ok';
        })()
"@ -AwaitPromise $false
    if ($picked -ne 'ok') { throw "test-owned row $Symbol not found on the catalogue page" }
    Wait-For 'row selected for delete' "document.querySelector('.te-toolbar .te-small').textContent.trim() === '1 selected on this page'"
    Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === 'Delete selected').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'confirm panel' "!!document.querySelector('.te-confirm-list')"
    Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim().indexOf('Delete 1 instrument') === 0).click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'rows decreased' "document.querySelectorAll('table[aria-label=`"Watched instruments`"] tbody tr').length < $rowsBefore" -TimeoutSec 20
    $rowsAfter = Invoke-Js "document.querySelectorAll('table[aria-label=`"Watched instruments`"] tbody tr').length" -AwaitPromise $false
    Write-Host "  -- rows after delete: $rowsAfter"
    Check 'row count decreased' ($rowsAfter -lt $rowsBefore)

    Check 'delete POST observed' (
        (Invoke-Js "window.__apiCalls.filter(c => c.method === 'POST' && c.url.indexOf('bulk-delete') >= 0).length" -AwaitPromise $false) -ge 1)
}
finally {
    if ($null -ne $instrumentId) {
        try {
            if (Remove-TestInstrument -Id $instrumentId) {
                Write-Host "  -- cleanup: deleted test-owned instrument $instrumentId"
            }
        } catch {
            Write-Host "  -- cleanup skipped: $($_.Exception.Message)"
        }
    }
    if ($null -ne $fingerprintId) {
        try {
            Invoke-LocalSql -NonQuery 'DELETE FROM WatchedInstruments WHERE Id = @id' @{ id = [Guid]$fingerprintId } | Out-Null
            Write-Host '  -- cleanup: removed probe row'
        } catch {
            Write-Host "  -- probe cleanup skipped: $($_.Exception.Message)"
        }
    }
    try { if ($null -ne $script:ws) { $script:ws.Dispose() } } catch { }
    try { if ($null -ne $edgeProc) { Stop-Process -Id $edgeProc.Id -Force -ErrorAction SilentlyContinue } } catch { }
    Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" |
        Where-Object { $_.CommandLine -like "*$profileDir*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    try { Remove-Item -Recurse -Force $profileDir -ErrorAction SilentlyContinue } catch { }
}

Write-Host ''
Write-Host '=== SUMMARY ==='
$script:results | Format-Table -AutoSize
$failed = @($script:results | Where-Object { $_.Result -eq 'FAIL' }).Count
Write-Host "$($script:results.Count - $failed)/$($script:results.Count) passed"
exit $failed
