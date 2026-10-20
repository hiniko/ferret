# Security policy

## Supported versions

Only the latest minor release of Ferret receives security fixes.

## Reporting a vulnerability

Please report vulnerabilities privately through GitHub Security Advisories:
<https://github.com/hiniko/ferret/security/advisories/new>

Do not open a public issue for a suspected vulnerability.

## Verifying releases

Release packages carry build provenance attestations. The attested files are attached to each
GitHub release; verify them with:

```sh
gh release download <tag> --repo hiniko/ferret -p '*.nupkg'
gh attestation verify <package>.nupkg --repo hiniko/ferret
```

Copies on nuget.org are repository-signed by nuget.org, so they will not match the attested
files byte-for-byte; verify the GitHub release assets instead.
