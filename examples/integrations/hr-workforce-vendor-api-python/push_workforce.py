#!/usr/bin/env python3
"""Push a weekly workforce snapshot to Ingest from an HR system's REST API.

A MINIMAL, dependency-free example of the "vendor API" integration style for HR
data. It GETs an already-aggregated weekly summary from an HR/payroll platform's
REST endpoint (e.g. MHR iTrent, Zellis ResourceLink, Civica HR), maps it to the
`weekly_workforce` schema, and POSTs one weekly submission.

For a self-contained run, the default source is a local static file served by
`python -m http.server` (see README). Point --source-url at the real HR endpoint
in production.

Usage:
    set INGEST_BASE_URL=https://ingest.example.org
    set INGEST_API_KEY=abc12345.your-secret-here
    python push_workforce.py [--source-url URL] [--dry-run]
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.error
import urllib.request

SCHEMA_NAME = "weekly_workforce"
DEFAULT_SOURCE_URL = "http://localhost:8000/sample_response.json"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Submit a weekly workforce snapshot to Ingest.")
    parser.add_argument(
        "--source-url",
        default=os.environ.get("SOURCE_URL", DEFAULT_SOURCE_URL),
        help="HR API URL returning the weekly summary JSON.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Build and print the payload but do not call the Ingest API.",
    )
    return parser.parse_args()


def fetch_summary(url: str) -> dict[str, object]:
    request = urllib.request.Request(url, method="GET")
    with urllib.request.urlopen(request) as response:
        return json.loads(response.read().decode("utf-8"))


def build_samples(summary: dict[str, object]) -> list[dict[str, object]]:
    headcount = summary["headcount"]
    # Weekly cadence: one sample per week bucket, keyed on the week-ending date.
    timestamp = f"{summary['weekEnding']}T00:00:00Z"

    def sample(value_name: str, value: object) -> dict[str, object]:
        return {
            "schemaName": SCHEMA_NAME,
            "valueName": value_name,
            "value": value,
            "timestamp": timestamp,
            "note": None,
        }

    samples = [
        sample("employees_active", int(headcount["permanentActive"])),
        sample("sick_leave", int(headcount["onSickLeave"])),
        sample("contractors", int(headcount["contractorsActive"])),
    ]

    # overtime_hours is optional: only send when there was overtime to report.
    overtime = float(summary.get("overtimeHours") or 0)
    if overtime > 0:
        samples.append(sample("overtime_hours", overtime))

    return samples


def post_submission(base_url: str, api_key: str, body: dict[str, object]) -> None:
    url = base_url.rstrip("/") + "/api/submissions"
    data = json.dumps(body).encode("utf-8")
    request = urllib.request.Request(
        url,
        data=data,
        method="POST",
        headers={"Content-Type": "application/json", "X-Api-Key": api_key},
    )
    try:
        with urllib.request.urlopen(request) as response:
            payload = json.loads(response.read().decode("utf-8"))
            print(f"Created submission {payload['id']}")
            for warning in payload.get("warnings", []):
                print(f"  warning: {warning}")
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8")
        print(f"Submission failed: HTTP {error.code}", file=sys.stderr)
        try:
            problem = json.loads(detail)
            for err in problem.get("errors", []):
                print(f"  error: {err}", file=sys.stderr)
            if not problem.get("errors"):
                print(f"  {problem.get('detail', detail)}", file=sys.stderr)
        except json.JSONDecodeError:
            print(f"  {detail}", file=sys.stderr)
        raise SystemExit(1)


def main() -> None:
    args = parse_args()
    summary = fetch_summary(args.source_url)
    body = {"samples": build_samples(summary)}

    if args.dry_run:
        print(json.dumps(body, indent=2))
        return

    base_url = os.environ.get("INGEST_BASE_URL")
    api_key = os.environ.get("INGEST_API_KEY")
    if not base_url or not api_key:
        raise SystemExit("Set INGEST_BASE_URL and INGEST_API_KEY (or use --dry-run).")

    post_submission(base_url, api_key, body)


if __name__ == "__main__":
    main()
