#!/usr/bin/env python3
"""Pull a weekly workforce summary from MHR iTrent's OData API and push it to Ingest.

A MINIMAL, dependency-free example of the "vendor API" integration style for HR
data, aimed at MHR iTrent. iTrent exposes OData feeds, so you query just the few
columns you need with $select, map them to the `weekly_workforce` schema, and POST
one weekly submission.

For a self-contained run, the default source is the bundled sample served by
`python -m http.server` (see README). Point --source-url at the real iTrent OData
endpoint in production.

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
    parser = argparse.ArgumentParser(description="Submit a weekly workforce snapshot to Ingest from iTrent.")
    parser.add_argument(
        "--source-url",
        default=os.environ.get("SOURCE_URL", DEFAULT_SOURCE_URL),
        help="iTrent OData URL returning the weekly summary JSON.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Build and print the payload but do not call the Ingest API.",
    )
    return parser.parse_args()


def fetch_week(url: str) -> dict[str, object]:
    # In production the URL carries the OData query, e.g.
    # .../odata/v1/WeeklyWorkforceSummary?$select=activeEmployees,absenceSickness,contingentWorkers,overtimeHours&$filter=organisationUnit eq 'Waste Services'
    request = urllib.request.Request(url, method="GET")
    with urllib.request.urlopen(request) as response:
        payload = json.loads(response.read().decode("utf-8"))
    # OData returns matching rows under "value"; one team-week is one row.
    rows = payload.get("value") or []
    if not rows:
        raise SystemExit("iTrent returned no rows for this week - nothing to submit.")
    return rows[0]


def build_samples(row: dict[str, object]) -> list[dict[str, object]]:
    # Weekly cadence: one sample per week bucket, keyed on the week-ending date.
    timestamp = f"{row['weekEnding']}T00:00:00Z"

    def sample(value_name: str, value: object) -> dict[str, object]:
        return {
            "schemaName": SCHEMA_NAME,
            "valueName": value_name,
            "value": value,
            "timestamp": timestamp,
            "note": None,
        }

    # Map the iTrent columns -> schema values.
    samples = [
        sample("employees_active", int(row["activeEmployees"])),
        sample("sick_leave", int(row["absenceSickness"])),
        sample("contractors", int(row["contingentWorkers"])),
    ]

    # overtime_hours is optional: only send when there was overtime to report.
    overtime = float(row.get("overtimeHours") or 0)
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
    row = fetch_week(args.source_url)
    body = {"samples": build_samples(row)}

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
