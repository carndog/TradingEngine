import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import unittest

SCRIPT = Path(__file__).with_name("validate-weekly-report-settings.py")
SPEC = importlib.util.spec_from_file_location("weekly_settings", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class WeeklySettingsTests(unittest.TestCase):
    def test_disabled_defaults_and_pause_identity_are_valid(self):
        MODULE.validate({}, "", "")
        MODULE.validate({"enabled": False, "deploymentId": "synthetic"}, "", "")

    def test_weekly_only_enabled_requires_credentials_and_private_settings(self):
        settings = {"enabled": True, "deploymentId": "synthetic", "baseUrl": "https://example.test", "recipientAddresses": ["recipient@example.test"]}
        MODULE.validate(settings, "synthetic-connection", "sender@example.test")
        with self.assertRaises(ValueError):
            MODULE.validate(settings, "", "")

    def test_malformed_and_unbounded_settings_are_rejected(self):
        cases = [[], {"enabled": "true"}, {"maxSnapshotRows": 100001}, {"localTime": "25:00"},
                 {"timeZone": "unknown-zone"}, {"baseUrl": "https://user@example.test"},
                 {"recipientAddresses": ["a@example.test", "A@example.test"]}, {"unknown": "synthetic"},
                 {"enabled": True}, {"enabled": True, "deploymentId": "synthetic"}]
        for settings in cases:
            with self.subTest(settings=settings), self.assertRaises(ValueError):
                MODULE.validate(settings, "synthetic-connection", "sender@example.test")

    def test_cli_failure_never_discloses_configuration(self):
        marker = "synthetic-private-marker"
        result = subprocess.run([sys.executable, str(SCRIPT)], env={**os.environ, "WEEKLY_REPORT_SETTINGS": '{"secret": "' + marker + '"}'},
                                capture_output=True, text=True, check=False)
        self.assertEqual(result.returncode, 1)
        self.assertNotIn(marker, result.stdout + result.stderr)
        self.assertIn("Values are not printed", result.stderr)

    def test_whatif_summary_redacts_weekly_setting_paths_and_values(self):
        fixture = {"changes": [{"resourceId": "synthetic", "delta": [{"path": "properties.WeeklyReports__RecipientAddresses__0",
                   "propertyChangeType": "Modify", "after": "recipient@example.test"}]}]}
        result = subprocess.run(["jq", "-r", "-f", str(SCRIPT.with_name("whatif-summary.jq"))], input=json.dumps(fixture),
                                capture_output=True, text=True, check=True)
        self.assertIn("private app setting path redacted", result.stdout)
        self.assertNotIn("WeeklyReports__", result.stdout)
        self.assertNotIn("recipient@example.test", result.stdout)


if __name__ == "__main__":
    unittest.main()
