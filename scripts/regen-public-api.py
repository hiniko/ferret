#!/usr/bin/env python3
"""
Bulk-regenerate PublicAPI.Unshipped.txt for all packable projects.

Use when a refactor invalidates the public surface (rename, namespace move,
record-shape change) and per-entry IDE code-fixes are too slow.

Flow:
  1. Resets every src/*/PublicAPI.Unshipped.txt to '#nullable enable'.
  2. Builds the solution, parses RS0016 (missing entry) diagnostics, appends
     missing symbols to the matching project's Unshipped.txt.
  3. Repeats until no RS0016 remains or max_iter reached.

After running, review the diff before committing. RS0017 (declared-but-missing)
errors mean an old entry no longer matches a real symbol — the reset clears
those automatically.

Run from repo root: python3 scripts/regen-public-api.py
"""
import os
import re
import glob
import subprocess
import sys
import collections

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SLN = os.path.join(REPO_ROOT, "Ferret.slnx")
MAX_ITER = 5
PROJ_RE = re.compile(r"\[(/.+?\.csproj)\]")
SYM_RE = re.compile(r"Symbol '(.+?)' is not part")


def reset_unshipped():
    for f in glob.glob(os.path.join(REPO_ROOT, "src/*/PublicAPI.Unshipped.txt")):
        with open(f, "w") as fh:
            fh.write("#nullable enable\n")
    print("reset all PublicAPI.Unshipped.txt to #nullable enable")


def build_and_collect():
    """Run dotnet build, return list of (api_file, symbol)."""
    proc = subprocess.run(
        ["dotnet", "build", SLN, "--no-incremental"],
        capture_output=True, text=True, cwd=REPO_ROOT,
    )
    pairs = []
    for line in proc.stdout.splitlines():
        if "error RS0016" not in line:
            continue
        sym = SYM_RE.search(line)
        proj = PROJ_RE.search(line)
        if not (sym and proj):
            continue
        api = os.path.join(os.path.dirname(proj.group(1)), "PublicAPI.Unshipped.txt")
        pairs.append((api, sym.group(1)))
    return pairs


def append(pairs):
    by_file = collections.defaultdict(set)
    for api, sym in pairs:
        by_file[api].add(sym)
    total = 0
    for api, syms in by_file.items():
        existing = set()
        if os.path.exists(api):
            with open(api) as f:
                existing = {l.rstrip("\n") for l in f}
        new = sorted(s for s in syms if s not in existing)
        if not new:
            continue
        with open(api, "a") as f:
            f.write("\n".join(new) + "\n")
        print(f"  {os.path.relpath(api, REPO_ROOT)}: +{len(new)}")
        total += len(new)
    return total


def main():
    if "--no-reset" not in sys.argv:
        reset_unshipped()
    for i in range(1, MAX_ITER + 1):
        print(f"iter {i}: building...")
        pairs = build_and_collect()
        print(f"  RS0016 occurrences: {len(pairs)}")
        if not pairs:
            print("done — no more RS0016")
            return 0
        added = append(pairs)
        if added == 0:
            print("no new entries to add — bailing")
            return 1
    print(f"max iterations ({MAX_ITER}) hit — investigate manually")
    return 1


if __name__ == "__main__":
    sys.exit(main())
