#!/usr/bin/env python3
"""Push a day's waste-collection rounds to the Ingest service.

This is a MINIMAL, dependency-free example. It reads a round-level CSV export of
the kind a waste-management / in-cab system (e.g. Bartec Collective, Whitespace,
Echo, Yotta Alloy) produces at the end of the day, aggregates it into the daily
KPIs defined by the `garbage_collection` schema, and POSTs one submission.

Only the Python standard library is used so it runs anywhere Python 3.8+ is
installed, with no `pip install` step.

Usage:
    set INGEST_BASE_URL=https://ingest.example.org
    set INGEST_API_KEY=abc12345.your-secret-here
    python push_waste_rounds.py [--csv rounds_export_2026-06-15.csv] [--dry-run]
"""
from __future__ import annotations

import argparse
import csv
import json
import os
import sys
import urllib.error
import urllib.request

SCHEMA_NAME = "garbage_collection"


def parse_args() -> argparse.Namespace:
    here = os.path.dirname(os.path.abspath(__file__))
    parser = argparse.ArgumentParser(description="Submit daily waste-collection KPIs to Ingest.")
    parser.add_argument(
        "--csv",
        default=os.path.join(here, "rounds_export_2026-06-15.csv"),
        help="Path to the round-level CSV export (default: the bundled sample).",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Build and print the payload but do not call the API.",
    )
    return parser.parse_args()


def read_rounds(path: str) -> list[dict[str, str]]:
    with open(path, newline="", encoding="utf-8-sig") as handle:
        return list(csv.DictReader(handle))


def aggregate(rounds: list[dict[str, str]]) -> dict[str, object]:
    """Roll up per-round rows into the daily schema values.

    The mapping mirrors what an operator would otherwise type into the form:
    totals across all rounds for the day, plus a few conditional follow-up fields.
    """
    if not rounds:
        raise SystemExit("No rounds found in the export - nothing to submit.")

    completed = [r for r in rounds if r["status"].strip().lower() == "completed"]
    missed = [r for r in rounds if r["status"].strip().lower() == "missed"]

    general_tonnes = sum(float(r["general_waste_tonnes"] or 0) for r in rounds)
    recycling_tonnes = sum(float(r["recycling_tonnes"] or 0) for r in rounds)

    breakdown_rows = [r for r in rounds if r["vehicle_breakdown"].strip().upper() == "Y"]

    # Weighted average contamination across rounds that actually carried recycling.
    recycling_rounds = [r for r in rounds if float(r["recycling_tonnes"] or 0) > 0]
    if recycling_rounds:
        weighted = sum(
            float(r["recycling_tonnes"]) * float(r["recycling_contamination_pct"] or 0)
            for r in recycling_rounds
        )
        contamination_pct = round(weighted / recycling_tonnes, 2)
    else:
        contamination_pct = 0.0

    return {
        # tonnes_collected is the total measured at the gate, recycling included,
        # so the schema rule recycling <= total always holds.
        "tonnes_collected": round(general_tonnes + recycling_tonnes, 2),
        "routes_completed": len(completed),
        "routes_missed": len(missed),
        "routes_missed_reason": "; ".join(
            f"{r['round_name']}: {r['miss_reason']}" for r in missed if r["miss_reason"].strip()
        ),
        "vehicle_breakdowns": len(breakdown_rows),
        "breakdown_description": "; ".join(
            f"{r['vehicle_reg']} ({r['round_name']}): {r['breakdown_notes']}"
            for r in breakdown_rows
            if r["breakdown_notes"].strip()
        ),
        "recycling_tonnes_collected": round(recycling_tonnes, 2),
        "contamination_pct": contamination_pct,
    }


def build_samples(values: dict[str, object], timestamp: str) -> list[dict[str, object]]:
    """Map aggregated values to schema samples, honouring the schema's visibleIf rules.

    Conditional fields are only sent when their condition holds; sending them
    otherwise would be discarded by the server with a warning.
    """
    def sample(value_name: str, value: object, note: str | None = None) -> dict[str, object]:
        return {
            "schemaName": SCHEMA_NAME,
            "valueName": value_name,
            "value": value,
            "timestamp": timestamp,
            "note": note,
        }

    samples = [
        sample("tonnes_collected", values["tonnes_collected"]),
        sample("routes_completed", values["routes_completed"]),
        sample("routes_missed", values["routes_missed"]),
        sample("vehicle_breakdowns", values["vehicle_breakdowns"]),
        sample("recycling_tonnes_collected", values["recycling_tonnes_collected"]),
    ]

    # routes_missed_reason is required, but only visible when routes_missed > 0.
    if values["routes_missed"] > 0:
        samples.append(sample("routes_missed_reason", values["routes_missed_reason"]))

    # breakdown_description only when at least one vehicle broke down.
    if values["vehicle_breakdowns"] > 0:
        samples.append(sample("breakdown_description", values["breakdown_description"]))

    # contamination_pct only when there was recycling to contaminate.
    if values["recycling_tonnes_collected"] > 0:
        samples.append(sample("contamination_pct", values["contamination_pct"]))

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
    rounds = read_rounds(args.csv)
    collection_date = rounds[0]["collection_date"]
    # Daily cadence: one sample per day bucket. End-of-shift UTC timestamp.
    timestamp = f"{collection_date}T17:00:00Z"

    values = aggregate(rounds)
    body = {"samples": build_samples(values, timestamp)}

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
