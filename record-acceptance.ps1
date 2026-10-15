#Requires -Version 5.1
<#
.SYNOPSIS
  Records a browser walkthrough of the watched-instrument catalogue as an
  MJPEG AVI assembled from Chrome DevTools Protocol screencast frames.

  Uses the same synthetic seeding and local-database guards as
  acceptance.ps1: refuses to run unless -LocalDb/$env:TE_ACCEPTANCE_DB is the
  disposable local SQL connection string and the API's live instrument set
  provably matches it.
#>
param(
    [string]$BaseUrl = 'http://localhost:5068',
    [string]$Symbol = 'TACPT',
    [string]$LocalDb = $env:TE_ACCEPTANCE_DB,
    [string]$OutFile = 'acceptance-walkthrough.avi',
    [int]$Fps = 8,
    [int]$Width = 1280,
    [int]$Height = 800
)

$ErrorActionPreference = 'Stop'

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
        if ($NonQuery) { return $cmd.ExecuteNonQuery() }
        return $cmd.ExecuteScalar()
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

# ---------- seed synthetic records ----------

$fingerprintId = [Guid]::NewGuid().ToString()
Invoke-LocalSql -NonQuery @'
INSERT INTO WatchedInstruments (Id, Symbol, Exchange, QuoteCurrency, MonitoringState, SamplingIntervalSeconds, CreatedAt, LastChangedAt)
VALUES (@id, @symbol, 'XTEST', 'GBP', 'Configured', 60, @now, @now)
'@ @{ id = [Guid]$fingerprintId; symbol = 'ZPROBE-' + $fingerprintId.Substring(0, 8); now = [DateTime]::UtcNow } | Out-Null

$probe = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/watched-instruments?page=1&pageSize=50"
if (@($probe.items | ForEach-Object { $_.id.ToLowerInvariant() }) -notcontains $fingerprintId) {
    throw 'Probe row not visible via the API - the API is not reading the configured local database. Aborting.'
}
Invoke-LocalSql -NonQuery 'UPDATE WatchedInstruments SET DeletedAt = @now WHERE Id = @id' @{
    id = [Guid]$fingerprintId; now = [DateTime]::UtcNow } | Out-Null
Write-Host '  -- API verified against local database via probe row'

$instrumentBody = @{
    symbol                  = $Symbol
    exchange                = 'XTEST'
    quoteCurrency           = 'GBP'
    samplingIntervalSeconds = 60
    monitoringState         = 'configured'
    priceScale              = 4
    supportZones            = @(@{
        id = 'support-a'; lower = 95.0; level = 100.0; upper = 105.0
        conditions = @(
            @{ type = 'buy-zone'; actionId = 'publish-signal' },
            @{ type = 'support-loss'; actionId = 'publish-signal' })
    })
    resistanceZones         = @(@{
        id = 'resistance-a'; lower = 120.0; level = 125.0; upper = 130.0
        conditions = @(@{ type = 'breakout'; actionId = 'publish-signal' })
    })
} | ConvertTo-Json -Depth 8

$instrumentId = $null
$existing = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/watched-instruments?q=$Symbol&page=1&pageSize=10"
$match = $existing.items | Where-Object { $_.symbol -eq $Symbol } | Select-Object -First 1
if ($null -ne $match) {
    $instrumentId = $match.id
    Write-Host "  -- reusing synthetic instrument $Symbol id=$instrumentId"
} else {
    $created = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/watched-instruments" `
        -ContentType 'application/json' -Body $instrumentBody
    $instrumentId = $created.id
    Write-Host "  -- seeded synthetic instrument $Symbol id=$instrumentId"
}

$found = Invoke-LocalSql 'SELECT COUNT(*) FROM WatchedInstruments WHERE Id = @id' @{ id = [Guid]$instrumentId }
if ([int]$found -ne 1) {
    try { $strayRemoved = Remove-TestInstrument -Id $instrumentId } catch { $strayRemoved = $false }
    throw "Seeded instrument not present in the local database - API wrote elsewhere. Removed via API: $strayRemoved."
}

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
            id = 'seed-z'; lower = 90.0; level = 95.0; upper = 100.0
            conditions = @(
                @{ type = 'buy-zone'; actionId = 'act-observe' },
                @{ type = 'support-loss'; actionId = 'act-observe' })
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
    Write-Host "  -- reusing draft id=$draftId"
}

# ---------- browser + screencast ----------

$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($null -eq $edge) { throw 'msedge.exe not found' }

$profileDir = Join-Path $env:TEMP ('te-rec-' + [guid]::NewGuid().ToString('N'))
$framesDir = Join-Path $env:TEMP ('te-frames-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $framesDir | Out-Null
if (Get-NetTCPConnection -LocalPort 9334 -State Listen -ErrorAction SilentlyContinue) {
    throw 'Port 9334 is already in use - a previous headless Edge may still be running.'
}
$edgeProc = $null
$script:ws = $null
$script:frameSeq = 0

try {
    $edgeProc = Start-Process -PassThru -FilePath $edge -ArgumentList @(
        '--headless=new', '--remote-debugging-port=9334', "--user-data-dir=$profileDir",
        '--no-first-run', '--no-default-browser-check', "--window-size=$Width,$Height", 'about:blank')

    $pageTarget = $null
    foreach ($i in 1..80) {
        try {
            $targets = Invoke-RestMethod -Uri 'http://127.0.0.1:9334/json' -TimeoutSec 2
            $pageTarget = $targets | Where-Object { $_.type -eq 'page' -and $_.url -eq 'about:blank' } | Select-Object -First 1
            if ($null -eq $pageTarget) { $pageTarget = $targets | Where-Object { $_.type -eq 'page' } | Select-Object -First 1 }
            if ($null -ne $pageTarget) { break }
        } catch { }
        Start-Sleep -Milliseconds 250
    }
    if ($null -eq $pageTarget) { throw 'no CDP page target' }

    $script:ws = [System.Net.WebSockets.ClientWebSocket]::new()
    $script:ws.ConnectAsync([Uri]$pageTarget.webSocketDebuggerUrl, [Threading.CancellationToken]::None).Wait()

    $script:msgId = 0
    function Send-RawCdp {
        param([int]$Id, [string]$Method, [hashtable]$Params = @{})
        $msg = @{ id = $Id; method = $Method; params = $Params } | ConvertTo-Json -Depth 10 -Compress
        $bytes = [Text.Encoding]::UTF8.GetBytes($msg)
        $script:ws.SendAsync([ArraySegment[byte]]::new($bytes), 'Text', $true, [Threading.CancellationToken]::None).Wait()
    }

    function Receive-One {
        $accum = ''
        while ($true) {
            $buf = New-Object byte[] 1048576
            $seg = [ArraySegment[byte]]::new($buf)
            $res = $script:ws.ReceiveAsync($seg, [Threading.CancellationToken]::None)
            $res.Wait()
            if ($res.Result.MessageType -eq 'Close' -or $res.Result.Count -eq 0) { return $null }
            $accum += [Text.Encoding]::UTF8.GetString($buf, 0, $res.Result.Count)
            if ($res.Result.EndOfMessage) {
                return ($accum | ConvertFrom-Json)
            }
        }
    }

    function Save-FrameEvent {
        param($Event)
        $script:frameSeq++
        $path = Join-Path $framesDir ('{0:D5}.jpg' -f $script:frameSeq)
        [IO.File]::WriteAllBytes($path, [Convert]::FromBase64String($Event.params.data))
        $ts = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() / 1000.0
        [IO.File]::AppendAllText("$framesDir\frames.tsv", "$script:frameSeq`t$ts`n")
        Send-RawCdp -Id (1000000 + $script:frameSeq) -Method 'Page.screencastFrameAck' -Params @{ sessionId = $Event.params.sessionId }
    }

    function Send-Cdp {
        param([string]$Method, [hashtable]$Params = @{})
        $script:msgId++
        $id = $script:msgId
        Send-RawCdp -Id $id -Method $Method -Params $Params
        $receives = 0
        while ($true) {
            $json = Receive-One -TimeoutMs 15000
            if ($null -eq $json) { throw "CDP timeout/closed while waiting for $Method" }
            if ($json.method -eq 'Page.screencastFrame') {
                Save-FrameEvent $json
            } elseif ($json.id -eq $id) {
                return $json
            }
            if (++$receives -gt 5000) { throw "No CDP response to $Method" }
        }
    }

    function Invoke-Js {
        param([string]$Expression, [bool]$AwaitPromise = $true)
        $r = Send-Cdp -Method 'Runtime.evaluate' -Params @{
            expression = $Expression; awaitPromise = $AwaitPromise; returnByValue = $true }
        if ($r.result.exceptionDetails) {
            throw "JS error: $($r.result.exceptionDetails.text) $($r.result.exceptionDetails.exception.description)"
        }
        return $r.result.result.value
    }

    function Wait-For {
        param([string]$Description, [string]$Expression, [int]$TimeoutSec = 20)
        $deadline = (Get-Date).AddSeconds($TimeoutSec)
        while ((Get-Date) -lt $deadline) {
            try {
                if ((Invoke-Js -Expression $Expression -AwaitPromise $false) -eq $true) {
                    Write-Host "  -- check ok: $Description"
                    return
                }
            } catch { }
            Start-Sleep -Milliseconds 150
        }
        throw "timed out waiting for: $Description"
    }

    function Pump-Frames {
        param([double]$Seconds)
        $deadline = (Get-Date).AddSeconds($Seconds)
        while ((Get-Date) -lt $deadline) {
            try {
                Invoke-Js "(function(){var d=document.getElementById('__rec');if(!d){d=document.createElement('div');d.id='__rec';d.style.cssText='position:fixed;right:0;bottom:0;width:2px;height:2px;z-index:2147483647';(document.body||document.documentElement).appendChild(d)}d.style.backgroundColor=(d.style.backgroundColor==='rgb(0, 0, 1)')?'#000000':'#000001';return 0})()" -AwaitPromise $false | Out-Null
            } catch { }
        }
    }

    Send-Cdp -Method 'Page.enable' | Out-Null
    Send-Cdp -Method 'Emulation.setDeviceMetricsOverride' -Params @{
        width = $Width; height = $Height; deviceScaleFactor = 1; mobile = $false } | Out-Null
    Send-Cdp -Method 'Page.startScreencast' -Params @{
        format = 'jpeg'; quality = 75; maxWidth = $Width; maxHeight = $Height; everyNthFrame = 1 } | Out-Null
    Write-Host '  -- recording started (pre-navigation)'
    Send-Cdp -Method 'Page.navigate' -Params @{ url = "$BaseUrl/" } | Out-Null
    Wait-For 'catalogue rows' "document.querySelectorAll('table[aria-label=`"Watched instruments`"] tbody tr').length > 0" -TimeoutSec 60

    # 1. catalogue + session header
    Wait-For 'session header' "((document.querySelector('.te-session')||{innerText:''}).innerText||'').indexOf('Local development') >= 0"
    Pump-Frames 2.0

    # 2. select a row, then type into search - selection clears immediately
    Invoke-Js "document.querySelector('table tbody input[type=checkbox]').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'one selected' "document.querySelector('.te-toolbar .te-small').textContent.trim() === '1 selected on this page'"
    Pump-Frames 1.2

    $searchSymbol = Invoke-Js "document.querySelector('tbody .te-open').textContent.trim()" -AwaitPromise $false
    foreach ($i in 1..$searchSymbol.Length) {
        $partial = $searchSymbol.Substring(0, $i)
        Invoke-Js @"
            (function(){
                const s = document.querySelector('input[type=search]');
                const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
                setter.call(s, '$partial');
                s.dispatchEvent(new Event('input', { bubbles: true }));
            })();
            'typed'
"@ -AwaitPromise $false | Out-Null
        Pump-Frames 0.12
    }
    Wait-For 'filtered' "document.querySelector('tbody .te-open') && document.querySelector('tbody .te-open').textContent.trim().indexOf('$searchSymbol') >= 0 && document.querySelector('.te-toolbar .te-small').textContent.trim() === '0 selected on this page'"
    Pump-Frames 1.5

    # 3. deletion confirmation, then cancel
    Invoke-Js "document.querySelector('table tbody input[type=checkbox]').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 're-selected' "document.querySelector('.te-toolbar .te-small').textContent.trim() === '1 selected on this page'"
    Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === 'Delete selected').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'confirm panel' "!!document.querySelector('.te-confirm-list')"
    Pump-Frames 2.0
    Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === 'Cancel').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'panel closed' "!document.querySelector('.te-confirm-list')"
    Pump-Frames 1.0

    # 4. timestamp editor + precision note
    Send-Cdp -Method 'Page.navigate' -Params @{ url = "$BaseUrl/instruments/$instrumentId/drafts/$draftId" } | Out-Null
    Wait-For 'datetime controls' "document.querySelectorAll('input[type=`"datetime-local`"]').length > 0" -TimeoutSec 25
    Pump-Frames 2.0
    Invoke-Js "document.querySelector('input[type=`"datetime-local`"]').focus(); 'ok'" -AwaitPromise $false | Out-Null
    Pump-Frames 1.5

    # 5. successful deletion of the test-owned instrument
    Send-Cdp -Method 'Page.navigate' -Params @{ url = "$BaseUrl/" } | Out-Null
    Wait-For 'catalogue back' "document.querySelectorAll('table[aria-label=`"Watched instruments`"] tbody tr').length > 0" -TimeoutSec 25
    Pump-Frames 1.0
    $picked = Invoke-Js @"
        (function(){
            const row = Array.from(document.querySelectorAll('tbody tr'))
                .find(tr => ((tr.querySelector('.te-open')||{}).textContent||'').trim() === '$Symbol');
            if (!row) { return 'missing'; }
            row.querySelector('input[type=checkbox]').click();
            return 'ok';
        })()
"@ -AwaitPromise $false
    if ($picked -ne 'ok') { throw "test-owned row $Symbol not found" }
    Wait-For 'selected for delete' "document.querySelector('.te-toolbar .te-small').textContent.trim() === '1 selected on this page'"
    Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === 'Delete selected').click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'confirm shown' "!!document.querySelector('.te-confirm-list')"
    Pump-Frames 1.5
    Invoke-Js "Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim().indexOf('Delete 1 instrument') === 0).click(); 'ok'" -AwaitPromise $false | Out-Null
    Wait-For 'deleted notice' "document.body.innerText.indexOf('Deleted 1 instrument') >= 0"
    Pump-Frames 2.5

    Send-Cdp -Method 'Page.stopScreencast' | Out-Null
    Write-Host "  -- recording stopped: $script:frameSeq frames"
}
finally {
    if ($null -ne $instrumentId) {
        try {
            if (Remove-TestInstrument -Id $instrumentId) {
                Write-Host "  -- cleanup: deleted test-owned instrument $instrumentId"
            }
        } catch { Write-Host "  -- cleanup skipped: $($_.Exception.Message)" }
    }
    try {
        Invoke-LocalSql -NonQuery 'DELETE FROM WatchedInstruments WHERE Id = @id' @{ id = [Guid]$fingerprintId } | Out-Null
        Write-Host '  -- cleanup: removed probe row'
    } catch { Write-Host "  -- probe cleanup skipped: $($_.Exception.Message)" }
    try { if ($null -ne $script:ws) { $script:ws.Dispose() } } catch { }
    try { if ($null -ne $edgeProc) { Stop-Process -Id $edgeProc.Id -Force -ErrorAction SilentlyContinue } } catch { }
    Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" |
        Where-Object { $_.CommandLine -like "*$profileDir*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    try { Remove-Item -Recurse -Force $profileDir -ErrorAction SilentlyContinue } catch { }
}

# ---------- assemble MJPEG AVI ----------

$frameFiles = @(Get-ChildItem "$framesDir\*.jpg" | Sort-Object Name)
if ($frameFiles.Count -eq 0) { throw 'no screencast frames captured' }
Write-Host "  -- assembling AVI from $($frameFiles.Count) captured frames"

$stamps = @{}
if (Test-Path "$framesDir\frames.tsv") {
    foreach ($line in [IO.File]::ReadAllLines("$framesDir\frames.tsv")) {
        $parts = $line -split "`t"
        if ($parts.Count -eq 2) { $stamps[[int]$parts[0]] = [double]$parts[1] }
    }
}

$jpegList = [System.Collections.Generic.List[byte[]]]::new()
foreach ($f in $frameFiles) { $jpegList.Add([IO.File]::ReadAllBytes($f.FullName)) }
$expanded = [System.Collections.Generic.List[byte[]]]::new()
$maxHoldFrames = [int][math]::Max(1, $Fps * 6)
for ($i = 0; $i -lt $jpegList.Count; $i++) {
    $repeats = 1
    if ($stamps.ContainsKey($i + 1) -and $stamps.ContainsKey($i + 2)) {
        $holdSec = $stamps[$i + 2] - $stamps[$i + 1]
        $repeats = [int][math]::Min($maxHoldFrames, [math]::Max(1, [math]::Round($holdSec * $Fps)))
    } elseif ($i -eq $jpegList.Count - 1) {
        $repeats = [int][math]::Max(1, [math]::Round($Fps * 0.8))
    }
    for ($r = 0; $r -lt $repeats; $r++) { $expanded.Add($jpegList[$i]) }
}

$frames = $expanded.ToArray()
$maxFrame = [int]($frames | ForEach-Object { $_.Length } | Measure-Object -Maximum | Select-Object -Expand Maximum)
$frameCount = $frames.Count
$usPerFrame = [uint32]([math]::Floor(1000000 / $Fps))

$fs = [IO.File]::Create($OutFile)
$w = [System.IO.BinaryWriter]::new($fs)

$w.Write([Text.Encoding]::ASCII.GetBytes('RIFF'))
$w.Write([uint32]0)
$w.Write([Text.Encoding]::ASCII.GetBytes('AVI '))

$w.Write([Text.Encoding]::ASCII.GetBytes('LIST'))
$w.Write([uint32](4 + 8 + 56 + 8 + 4 + 8 + 64 + 8 + 40))
$w.Write([Text.Encoding]::ASCII.GetBytes('hdrl'))

$w.Write([Text.Encoding]::ASCII.GetBytes('avih'))
$w.Write([uint32]56)
$w.Write([uint32]$usPerFrame)
$w.Write([uint32]0)
$w.Write([uint32]0)
$w.Write([uint32]0x10)
$w.Write([uint32]$frameCount)
$w.Write([uint32]0)
$w.Write([uint32]1)
$w.Write([uint32]$maxFrame)
$w.Write([uint32]$Width)
$w.Write([uint32]$Height)
1..4 | ForEach-Object { $w.Write([uint32]0) }

$w.Write([Text.Encoding]::ASCII.GetBytes('LIST'))
$w.Write([uint32](4 + 8 + 64 + 8 + 40))
$w.Write([Text.Encoding]::ASCII.GetBytes('strl'))

$w.Write([Text.Encoding]::ASCII.GetBytes('strh'))
$w.Write([uint32]64)
$w.Write([Text.Encoding]::ASCII.GetBytes('vids'))
$w.Write([Text.Encoding]::ASCII.GetBytes('MJPG'))
$w.Write([uint32]0)
$w.Write([uint16]0)
$w.Write([uint16]0)
$w.Write([uint32]0)
$w.Write([uint32]1)
$w.Write([uint32]$Fps)
$w.Write([uint32]0)
$w.Write([uint32]$frameCount)
$w.Write([uint32]$maxFrame)
$w.Write([uint32]::MaxValue)
$w.Write([uint32]0)
$w.Write([int32]0); $w.Write([int32]0); $w.Write([int32]$Width); $w.Write([int32]$Height)

$w.Write([Text.Encoding]::ASCII.GetBytes('strf'))
$w.Write([uint32]40)
$w.Write([int32]40)
$w.Write([int32]$Width)
$w.Write([int32]$Height)
$w.Write([int16]1)
$w.Write([int16]24)
$w.Write([Text.Encoding]::ASCII.GetBytes('MJPG'))
$w.Write([uint32]$maxFrame)
$w.Write([uint32]0)
$w.Write([uint32]0)
$w.Write([uint32]0)
$w.Write([uint32]0)

$w.Write([Text.Encoding]::ASCII.GetBytes('LIST'))
$moviSizePos = $fs.Position
$w.Write([uint32]0)
$w.Write([Text.Encoding]::ASCII.GetBytes('movi'))
$moviDataStart = $fs.Position

$offsets = [System.Collections.Generic.List[uint32]]::new()
foreach ($frame in $frames) {
    $offsets.Add([uint32]($fs.Position - $moviDataStart))
    $w.Write([Text.Encoding]::ASCII.GetBytes('00dc'))
    $w.Write([uint32]$frame.Length)
    $w.Write($frame)
    if ($frame.Length % 2 -eq 1) { $w.Write([byte]0) }
}
$moviEnd = $fs.Position

$w.Write([Text.Encoding]::ASCII.GetBytes('idx1'))
$w.Write([uint32]($frameCount * 16))
for ($i = 0; $i -lt $frameCount; $i++) {
    $w.Write([Text.Encoding]::ASCII.GetBytes('00dc'))
    $w.Write([uint32]0x10)
    $w.Write([uint32]$offsets[$i])
    $w.Write([uint32]$frames[$i].Length)
}

$fileEnd = $fs.Position
$fs.Position = $moviSizePos
$w.Write([uint32]($moviEnd - $moviSizePos - 4))
$fs.Position = 4
$w.Write([uint32]($fileEnd - 8))
$w.Flush()
$fs.Close()

Remove-Item -Recurse -Force $framesDir -ErrorAction SilentlyContinue
$mb = [math]::Round($fileEnd / 1MB, 2)
Write-Host "  -- wrote $OutFile ($mb MB, $frameCount frames @ $Fps fps, $Widthx$Height)"
