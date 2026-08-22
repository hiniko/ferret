#!/usr/bin/env python3
"""
Promote PublicAPI.Unshipped.txt -> PublicAPI.Shipped.txt for all packable projects.

Run this immediately BEFORE tagging a release, then commit the result and tag
that commit. Once a symbol is in Shipped.txt, the public-API analyzer treats
removing or changing it as an RS0017 error — that is what turns the analyzer
into breaking-change detection instead of a bookkeeping exercise.

This cannot live in the release workflow: that workflow checks out an existing
tag, so anything it changed would land after the tag it is meant to describe.

Flow:
  1. For each src/*/PublicAPI.Shipped.txt, append every entry currently in the
     matching Unshipped.txt.
  2. Sort + de-duplicate Shipped.txt, keeping the '#nullable enable' header.
  3. Reset Unshipped.txt to just the header.

Idempotent: running twice in a row is a no-op the second time (nothing left to
promote). Verify with `dotnet build` afterwards — a clean build means the
declared surface still matches the code.

Run from repo root:  python3 scripts/ship-public-api.py [--check]

  --check   Report what would be promoted and exit 1 if anything is pending.
            Intended for a pre-release guard; changes nothing.
"""
import glob
import os
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HEADER = "#nullable enable"


def read_entries(path):
    """Return (header_present, sorted set of real entries) for an API file."""
    if not os.path.exists(path):
        return False, set()
    lines = [l.rstrip("\n") for l in open(path, encoding="utf-8")]
    header = any(l.strip() == HEADER for l in lines)
    entries = {l for l in lines if l.strip() and l.strip() != HEADER}
    return header, entries


def write_entries(path, entries, header):
    body = [HEADER] if header else []
    body += sorted(entries)
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(body) + "\n")


def main():
    check_only = "--check" in sys.argv[1:]
    projects = sorted(glob.glob(os.path.join(REPO_ROOT, "src", "*", "PublicAPI.Unshipped.txt")))
    if not projects:
        print("no PublicAPI.Unshipped.txt files found under src/", file=sys.stderr)
        return 1

    total = 0
    pending = []
    for unshipped_path in projects:
        proj_dir = os.path.dirname(unshipped_path)
        name = os.path.basename(proj_dir)
        shipped_path = os.path.join(proj_dir, "PublicAPI.Shipped.txt")

        un_header, un_entries = read_entries(unshipped_path)
        sh_header, sh_entries = read_entries(shipped_path)

        if not un_entries:
            print(f"  {name}: nothing to promote")
            continue

        new = un_entries - sh_entries
        total += len(un_entries)
        pending.append((name, len(un_entries), len(new)))

        if check_only:
            continue

        # *REMOVED*X lines mark a symbol X leaving the shipped surface. Promoting a
        # removal means deleting the plain entry for X from Shipped — the *REMOVED*
        # marker itself never belongs in Shipped.txt (RS0024 rejects that: Shipped
        # is the "what's live" set, not a change log).
        removed = {e[len("*REMOVED*"):] for e in un_entries if e.startswith("*REMOVED*")}
        additions = un_entries - removed - {f"*REMOVED*{e}" for e in removed}
        shipped_next = (sh_entries - removed) | additions
        write_entries(shipped_path, shipped_next, sh_header or un_header or True)
        write_entries(unshipped_path, set(), un_header or True)
        print(f"  {name}: promoted {len(un_entries)} entries ({len(new)} new to Shipped)")

    if check_only:
        if pending:
            print(f"\n{total} API entries are unshipped across {len(pending)} project(s):")
            for name, count, new in pending:
                print(f"  {name}: {count} pending ({new} not yet in Shipped)")
            print("\nRun `python3 scripts/ship-public-api.py` before tagging.")
            return 1
        print("\nAll public API entries are shipped.")
        return 0

    print(f"\nPromoted {total} entries. Next:")
    print("  dotnet build Ferret.slnx -c Release   # confirm the surface still matches")
    print("  git add src/*/PublicAPI.*.txt && git commit")
    print("  git tag <version> && push, then run the release workflow")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
