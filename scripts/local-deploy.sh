#!/usr/bin/env bash
# Packs all Ferret library packages at VERSION and installs them into the global
# NuGet cache, so consumer repos restore the local build
# with no NuGet.config changes — cache hits skip source resolution entirely.
#
# The cached local build shadows any published package of the same version until
# you run: rm -rf ~/.nuget/packages/ferret.*  (then a normal restore pulls nuget.org).
#
# Usage: scripts/local-deploy.sh [version]   (default: 0.1.3)
set -euo pipefail

VERSION="${1:-0.1.3}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

dotnet pack "$ROOT/Ferret.slnx" -c Release -o "$WORK/pkgs" "/p:MinVerVersionOverride=$VERSION" --nologo

rm -rf "$HOME"/.nuget/packages/ferret.*

# A throwaway consumer restore is the supported way to install .nupkg files into
# the global cache. Ferret.Tools.Cli is a dotnet tool package and can't be a
# PackageReference, so it is packed but not seeded.
cat > "$WORK/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="pkgs" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

cat > "$WORK/seed.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Ferret.Abstractions" Version="$VERSION" />
    <PackageReference Include="Ferret.Core" Version="$VERSION" />
    <PackageReference Include="Ferret.EntityFrameworkCore" Version="$VERSION" />
    <PackageReference Include="Ferret.AspNetCore" Version="$VERSION" />
    <PackageReference Include="Ferret.Compat.LegacyApi" Version="$VERSION" />
    <PackageReference Include="Ferret.Migrations" Version="$VERSION" />
    <PackageReference Include="Ferret.Hosting" Version="$VERSION" />
    <PackageReference Include="Ferret.Hydration.Dapper" Version="$VERSION" />
  </ItemGroup>
</Project>
EOF

dotnet restore "$WORK/seed.csproj" --nologo

echo "Ferret $VERSION (local build of $(git -C "$ROOT" rev-parse --short HEAD)) installed into ~/.nuget/packages."
echo "Consumers pinning $VERSION now restore this build. Remember: dotnet restore --force in the consumer."
