#!/usr/bin/env python3
"""Regenerate src/NetPulse.Core/Data/oui.txt from the public IEEE registries.

Usage: python3 tools/update-oui.py

Downloads the MA-L, MA-M and MA-S CSV files published by the IEEE Registration Authority and writes
one line per assignment: "<hex prefix>\t<organization name>". Standard library only.
"""

import csv
import datetime
import io
import pathlib
import urllib.request

SOURCES = [
    "https://standards-oui.ieee.org/oui/oui.csv",      # MA-L, 24-bit prefixes
    "https://standards-oui.ieee.org/oui28/mam.csv",    # MA-M, 28-bit prefixes
    "https://standards-oui.ieee.org/oui36/oui36.csv",  # MA-S, 36-bit prefixes
]

OUTPUT = pathlib.Path(__file__).resolve().parent.parent / "src" / "NetPulse.Core" / "Data" / "oui.txt"


def download(url: str) -> str:
    request = urllib.request.Request(url, headers={"User-Agent": "NetPulse-oui-updater"})
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read().decode("utf-8", errors="replace")


def main() -> None:
    entries: dict[str, str] = {}
    for url in SOURCES:
        print(f"Downloading {url}")
        for row in csv.DictReader(io.StringIO(download(url))):
            prefix = row["Assignment"].strip().upper()
            name = " ".join(row["Organization Name"].split())
            if prefix and name:
                entries.setdefault(prefix, name)

    today = datetime.date.today().isoformat()
    lines = [
        "# NetPulse vendor database: MAC address prefix (hex) <TAB> organization name.",
        f"# Source: IEEE Registration Authority public listings (MA-L, MA-M, MA-S), snapshot of {today}.",
        "# Regenerate with: python3 tools/update-oui.py",
    ]
    lines += [f"{prefix}\t{entries[prefix]}" for prefix in sorted(entries)]

    OUTPUT.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Wrote {len(entries)} entries to {OUTPUT}")


if __name__ == "__main__":
    main()
