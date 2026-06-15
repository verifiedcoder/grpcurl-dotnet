#!/usr/bin/env python3
"""Enforce a line-coverage floor for one assembly across Cobertura reports.

`dotnet test --solution` with coverlet emits one Cobertura file per test assembly, each
covering only the lines that project exercised. This unions them by (filename, line) so a
line counts as covered when ANY report hit it, then checks the target package's line rate.

Usage:
    check-coverage-gate.py --coverage-dir <dir> --package <name> --min <0..1>
"""
import argparse
import glob
import os
import sys
import xml.etree.ElementTree as ET


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--coverage-dir", required=True)
    parser.add_argument("--package", required=True)
    parser.add_argument("--min", type=float, required=True)
    args = parser.parse_args()

    reports = glob.glob(os.path.join(args.coverage_dir, "**", "*cobertura*.xml"), recursive=True)
    if not reports:
        print(f"::error::No Cobertura reports found under {args.coverage_dir}")
        return 1

    covered: set[tuple[str, str]] = set()
    total: set[tuple[str, str]] = set()
    seen_package = False

    for report in reports:
        root = ET.parse(report).getroot()
        for package in root.iter("package"):
            if package.get("name") != args.package:
                continue
            seen_package = True
            for cls in package.iter("class"):
                filename = cls.get("filename")
                for line in cls.iter("line"):
                    key = (filename, line.get("number"))
                    total.add(key)
                    if int(line.get("hits", "0")) > 0:
                        covered.add(key)

    if not seen_package:
        print(f"::error::Package '{args.package}' not found in any Cobertura report")
        return 1

    rate = len(covered) / len(total) if total else 1.0
    print(f"{args.package}: line coverage {len(covered)}/{len(total)} = {rate:.1%} (floor {args.min:.0%})")

    if rate + 1e-9 < args.min:
        print(f"::error::Coverage gate failed: {rate:.1%} < {args.min:.0%} for {args.package}")
        return 1

    print("Coverage gate passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
