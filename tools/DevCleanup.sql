-- Targeted cleanup for one synthetic HTTP-walkthrough run in the Azure Dev
-- database. Do not run this file directly: tools/Remove-TradingEngineTestRun.ps1
-- drives it through sqlcmd and supplies the variables below.
--
--   $(RUN_ID)             run id logged by the .http walkthrough
--   $(EXPECTED_SERVER)    short name of the approved Azure Dev SQL server
--   $(EXPECTED_DATABASE)  database name the caller connected to
--   $(APPLY)              '1' deletes, '0' previews only
--
-- A run matches watched instruments whose Symbol is 'RUN-<runId>-<suffix>' on
-- exchange XTEST while still 'Configured'. Their MonitoringRuleRevisions,
-- MonitoringRules, ChartAnalysisDefinitions and WatchedInstruments rows are
-- deleted in one transaction in foreign-key order. Unrelated instruments,
-- monitored instruments and other run ids are preserved. Rerunning the same
-- run id is a safe no-op.

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @RunId nvarchar(64) = N'$(RUN_ID)';
DECLARE @ExpectedServer nvarchar(128) = N'$(EXPECTED_SERVER)';
DECLARE @ExpectedDatabase nvarchar(128) = N'$(EXPECTED_DATABASE)';
DECLARE @Apply bit = CAST(N'$(APPLY)' AS bit);

IF @RunId IS NULL OR LEN(@RunId) < 4 OR LEN(@RunId) > 32
    OR @RunId LIKE N'%[^0-9A-Z]%'
BEGIN
    ;THROW 51000, 'RUN_ID must be 4-32 characters of A-Z0-9.', 1;
END;

IF @ExpectedServer IS NULL OR @ExpectedServer = N''
    OR LOWER(@@SERVERNAME) <> LOWER(@ExpectedServer)
BEGIN
    ;THROW 51002, 'Connected server does not match the configured Azure Dev EXPECTED_SERVER.', 1;
END;

IF DB_NAME() <> @ExpectedDatabase
BEGIN
    ;THROW 51001, 'Connected database does not match EXPECTED_DATABASE.', 1;
END;

DECLARE @InstrumentIds TABLE (Id uniqueidentifier PRIMARY KEY);

INSERT @InstrumentIds (Id)
SELECT w.Id
FROM WatchedInstruments w
WHERE w.Symbol LIKE N'RUN-' + @RunId + N'-%'
  AND w.Exchange = N'XTEST'
  AND w.MonitoringState = N'Configured';

SELECT w.Id, w.Symbol, w.Exchange, w.QuoteCurrency, w.MonitoringState, w.CreatedAt
FROM WatchedInstruments w
JOIN @InstrumentIds target ON target.Id = w.Id
ORDER BY w.CreatedAt;

SELECT COUNT(*) AS RevisionRows
FROM MonitoringRuleRevisions r
JOIN MonitoringRules m ON m.Id = r.MonitoringRuleId
JOIN @InstrumentIds target ON target.Id = m.WatchedInstrumentId;

SELECT COUNT(*) AS RuleRows
FROM MonitoringRules m
JOIN @InstrumentIds target ON target.Id = m.WatchedInstrumentId;

SELECT COUNT(*) AS ChartAnalysisDefinitionRows
FROM ChartAnalysisDefinitions d
JOIN @InstrumentIds target ON target.Id = d.WatchedInstrumentId;

SELECT w.Id, w.Symbol, w.MonitoringState
FROM WatchedInstruments w
WHERE w.Symbol LIKE N'RUN-' + @RunId + N'-%'
  AND w.Id NOT IN (SELECT Id FROM @InstrumentIds)
ORDER BY w.CreatedAt;

IF @Apply = 0
BEGIN
    PRINT 'Preview only - rerun with -Apply to delete the rows listed above.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    DELETE r
    FROM MonitoringRuleRevisions r
    JOIN MonitoringRules m ON m.Id = r.MonitoringRuleId
    JOIN @InstrumentIds target ON target.Id = m.WatchedInstrumentId;

    DELETE m
    FROM MonitoringRules m
    JOIN @InstrumentIds target ON target.Id = m.WatchedInstrumentId;

    DELETE d
    FROM ChartAnalysisDefinitions d
    JOIN @InstrumentIds target ON target.Id = d.WatchedInstrumentId;

    DELETE w
    FROM WatchedInstruments w
    JOIN @InstrumentIds target ON target.Id = w.Id;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    ;THROW;
END CATCH;

DECLARE @Deleted int;
SELECT @Deleted = COUNT(*) FROM @InstrumentIds;
PRINT 'Deleted run ' + @RunId + ': ' + CAST(@Deleted AS nvarchar(10)) + ' instrument(s) removed.';
