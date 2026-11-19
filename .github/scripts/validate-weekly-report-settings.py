import json
import os
import re
import sys
from urllib.parse import urlparse
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError


def validate(settings, email_connection, email_sender):
    if not isinstance(settings, dict):
        raise ValueError("Weekly report settings must be a JSON object.")
    ranges = {
        "isoDayOfWeek": (1, 7), "pollIntervalSeconds": (1, 3600),
        "generationLeaseSeconds": (30, 3600), "claimLeaseSeconds": (30, 3600),
        "maxDispatchAttempts": (1, 20), "maxReportsPerRun": (1, 100),
        "maxSnapshotRows": (1, 100000), "maxEmailRows": (1, 1000), "maxEmailBytes": (4096, 1000000)
    }
    allowed = set(ranges) | {"enabled", "deploymentId", "subscriptionId", "timeZone", "localTime", "baseUrl", "recipientAddresses"}
    if set(settings) - allowed:
        raise ValueError("Weekly report settings contain an unsupported property.")
    enabled = settings.get("enabled", False)
    if type(enabled) is not bool:
        raise ValueError("Weekly report enabled must be a boolean.")
    for name, (lower, upper) in ranges.items():
        if name in settings and (type(settings[name]) is not int or not lower <= settings[name] <= upper):
            raise ValueError("Weekly report numeric settings are outside supported bounds.")
    for name in ("deploymentId", "subscriptionId"):
        value = settings.get(name, "operator" if name == "subscriptionId" else "")
        if not isinstance(value, str) or ((enabled or value or name == "subscriptionId") and not re.fullmatch(r"[A-Za-z0-9_-]{1,64}", value)):
            raise ValueError("Weekly reporting requires valid stable deployment/subscription identities.")
    local_time = settings.get("localTime", "18:00")
    if not isinstance(local_time, str) or not re.fullmatch(r"(?:[01][0-9]|2[0-3]):[0-5][0-9]", local_time):
        raise ValueError("Weekly report localTime must be HH:mm.")
    try:
        ZoneInfo(settings.get("timeZone", "Europe/London"))
    except (ZoneInfoNotFoundError, TypeError, ValueError):
        raise ValueError("Weekly report timeZone must be an IANA time zone.") from None
    origin = settings.get("baseUrl", "")
    if not isinstance(origin, str):
        raise ValueError("Weekly report baseUrl must be a string.")
    if enabled or origin:
        url = urlparse(origin)
        if url.scheme != "https" or not url.hostname or url.username or url.password or url.query or url.fragment or url.path not in ("", "/") or len(origin) > 2048:
            raise ValueError("Weekly report baseUrl must be the HTTPS application origin.")
    recipients = settings.get("recipientAddresses", [])
    if not isinstance(recipients, list) or len(recipients) > 50 or (enabled and not recipients):
        raise ValueError("Weekly report recipients must be a bounded array, nonempty when enabled.")
    if any(not isinstance(address, str) or len(address) > 254 or not re.fullmatch(r"[^\s@<>]+@[^\s@<>]+", address) for address in recipients):
        raise ValueError("Weekly report recipient syntax is invalid.")
    if len({address.lower() for address in recipients}) != len(recipients):
        raise ValueError("Weekly report recipients must be unique.")
    if enabled and (not email_connection.strip() or not email_sender.strip()):
        raise ValueError("Enabled weekly reports require ACS connection and sender secrets.")


if __name__ == "__main__":
    try:
        settings = json.loads(os.environ.get("WEEKLY_REPORT_SETTINGS") or "{}")
        validate(settings, os.environ.get("ACS_EMAIL_CONNECTION_STRING", ""), os.environ.get("ACS_EMAIL_SENDER_ADDRESS", ""))
    except (ValueError, TypeError):
        print("::error::Weekly report configuration is invalid; check the documented schema and required ACS secrets. Values are not printed.", file=sys.stderr)
        sys.exit(1)
