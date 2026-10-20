# Security policy

## Supported versions

Only the latest minor release of Ferret receives security fixes.

## Reporting a vulnerability

Please report vulnerabilities privately through GitHub Security Advisories:
<https://github.com/hiniko/ferret/security/advisories/new>

Do not open a public issue for a suspected vulnerability.

## Verifying releases

Published `.nupkg` files carry build provenance attestations. Verify a downloaded package with:

```sh
gh attestation verify <package>.nupkg --repo hiniko/ferret
```
