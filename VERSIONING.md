# Versioning

Ferret is **pre-1.0**. It is feature-complete and the core principles are exercised by a
live origin project, but it has not yet been battle-tested broadly. Until `1.0.0`, treat the
public API as **subject to change**.

The first published tag is `0.1.0` — early on purpose. We climb `0.x` as real use shakes
things out, and only cut `1.0.0` once we trust it.

## Pre-1.0 SemVer policy

While on `0.x`:

- **Minor** bump (`0.1.0` → `0.2.0`) — may include **breaking** API changes.
- **Patch** bump (`0.1.0` → `0.1.1`) — bug fixes and non-breaking additions only.

This is the standard pre-1.0 convention: it gives us room to fix design problems that only
surface under real use, without pretending the API is frozen.

## Road to 1.0

`1.0.0` is cut only when all of these hold:

1. A soak period of real-world use (origin project + at least one new project) with **no
   API-breaking surprises**.
2. `EntityFrameworkCore.ExtensibleMigrations` is published to NuGet (today `Ferret.Migrations`
   references it as a sibling-repo project reference — a publish blocker).
3. The public API is frozen: every `PublicAPI.Unshipped.txt` entry promoted to
   `PublicAPI.Shipped.txt`.

`1.0.0` is the API-stability commitment. After it, breaking changes require a major bump.

## Mechanics (MinVer)

Versions are **driven by git tags** via [MinVer](https://github.com/adamralph/minver) (no tag
prefix; tags are plain SemVer like `0.1.0`).

- A commit **at** a tag builds as that exact version (`0.1.0`).
- Commits **after** a tag build as the next patch pre-release with height, e.g.
  `0.1.1-alpha.0.5` (5 commits past `0.1.0`). These are NuGet pre-release versions.
- To cut a release, tag the chosen commit and build:
  ```bash
  git tag 0.1.1
  dotnet pack -c Release
  ```

NuGet treats a version as **pre-release only if it has a `-suffix`**. So `0.1.0` is a normal
(stable) package — being on `0.x` is the "handle with care" signal, per the policy above.
