#!/usr/bin/env python3
"""
Regenerates docs/benchmarks.md from the latest BenchmarkDotNet result files.

Run from the repository root:
    python3 .github/scripts/update-benchmarks-doc.py
"""

import os
import re
import sys
from datetime import datetime, timezone
from string import Template

RESULTS_DIR = os.path.join(
    "tests", "MediatorLite.Benchmarks", "BenchmarkDotNet.Artifacts", "results"
)
OUTPUT_FILE = os.path.join("docs", "benchmarks.md")

RESULT_FILES = [
    ("mediator", "MediatorBenchmarks-report-github.md"),
    ("pipeline", "PipelineBenchmarks-report-github.md"),
    ("multi",    "MultipleBehaviorsBenchmarks-report-github.md"),
    ("notif",    "NotificationBenchmarks-report-github.md"),
]

DOC_TEMPLATE = Template("""\
---
title: Benchmarks
nav_order: 8
---

# Benchmarks

Performance comparisons between MediatorLite and [MediatR](https://github.com/jbogard/MediatR) across \
four scenarios: simple request dispatch, single pipeline behavior, multiple pipeline behaviors, and \
notification publishing.

> Last updated: $run_date — automated via CI.
{: .note }

## Environment

```
$env
```

---

## Simple Request

Baseline scenario: a single request dispatched to a single handler with no pipeline behaviors.

$mediator_table

---

## Single Pipeline Behavior

Request dispatched through one open pipeline behavior (simulating a logging or tracing layer).

$pipeline_table

---

## Multiple Pipeline Behaviors

Request dispatched through three behaviors (logging, validation, metrics) — a typical production pipeline.

$multi_table

---

## Notifications

A notification published to three handlers, comparing MediatR against MediatorLite's `Sequential` \
and `Parallel` execution strategies.

$notif_table

---

## Key Takeaways

MediatorLite consistently allocates less memory across every scenario. The notification sequential \
mode is especially notable — near-identical latency to MediatR but less than half the allocations. \
The `ValueTask`-based pipeline and source-generated dispatch path trade raw per-call speed for a \
predictable, low-allocation footprint.

---

## Running Benchmarks Locally

```bash
cd tests/MediatorLite.Benchmarks
dotnet run --configuration Release -- --filter '*' --exporters json markdown --memory
```

Results are written to `BenchmarkDotNet.Artifacts/results/`.

## REST API Benchmarks (Production-Like)

The repository also includes a REST API benchmark project at `tests/MediatorLite.RestApiBenchmarks`.
This suite compares MediatorLite and MediatR in end-to-end API scenarios backed by SQLite:

- In-process transport (`TestServer`) and real localhost transport (`Kestrel`)
- Read-heavy and write-heavy API operations
- Concurrency scenarios with multiple in-flight requests

Run read/write benchmarks:

```bash
dotnet run -c Release --project tests/MediatorLite.RestApiBenchmarks -- --filter '*RestApiReadWriteBenchmarks*'
```

Run concurrency benchmarks:

```bash
dotnet run -c Release --project tests/MediatorLite.RestApiBenchmarks -- --filter '*RestApiConcurrencyBenchmarks*'
```
""")


def extract_env(content: str) -> str:
    """Extract the environment block from between the first pair of triple-backtick fences."""
    m = re.search(r"```\n(.*?)```", content, re.DOTALL)
    return m.group(1).strip() if m else ""


def extract_table(content: str) -> str:
    """Extract all pipe-table rows (lines starting with '|')."""
    lines = [line for line in content.splitlines() if line.startswith("|")]
    return "\n".join(lines)


def main() -> None:
    data: dict[str, dict[str, str]] = {}
    for key, fname in RESULT_FILES:
        path = os.path.join(RESULTS_DIR, fname)
        if not os.path.exists(path):
            print(f"Missing result file: {path} — skipping docs update.", file=sys.stderr)
            sys.exit(0)
        with open(path, encoding="utf-8") as f:
            text = f.read()
        data[key] = {"env": extract_env(text), "table": extract_table(text)}

    run_date = datetime.now(timezone.utc).strftime("%Y-%m-%d")

    doc = DOC_TEMPLATE.substitute(
        run_date=run_date,
        env=data["mediator"]["env"],
        mediator_table=data["mediator"]["table"],
        pipeline_table=data["pipeline"]["table"],
        multi_table=data["multi"]["table"],
        notif_table=data["notif"]["table"],
    )

    with open(OUTPUT_FILE, "w", encoding="utf-8") as f:
        f.write(doc)

    print(f"Updated {OUTPUT_FILE}")


if __name__ == "__main__":
    main()
