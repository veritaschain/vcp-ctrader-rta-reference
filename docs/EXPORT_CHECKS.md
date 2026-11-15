# Current local export checks

`VCPEvidencePack.ExportVerificationPackageAsync()` writes a `verify.py` script
alongside a JSON array in `batches.json`. Starting with the 15 November 2026 repair,
that script reports its actual scope and propagates failures through exit codes.

```bash
python /path/to/export/verify.py
python /path/to/export/verify.py /path/to/another/export
```

Without an argument, the script reads its own directory, independent of the
working directory. An explicit argument selects that export directory. `--help`
prints usage without inspecting evidence, and unexpected arguments are rejected.

## Checks and results

The script reads only `batches.json`. It accepts ordinary UTF-8 and the UTF-8 BOM
written by the C# export path. Every batch must have a distinct nonempty string
ID, an array of 32-byte hexadecimal event hashes, a matching integer `EventCount`
(not a boolean or floating-point count), and a 32-byte hexadecimal Merkle root.
It checks every supplied batch, so a later matching batch cannot erase an earlier
mismatch. Duplicate JSON members, non-finite numbers, exponent overflow, invalid
UTF-8 and malformed shapes are rejected without a traceback.

The root calculation preserves the existing SHA-256 prefixes and odd-node
duplication construction. It does not migrate old roots to RFC 6962; see
[construction limitations](MERKLE_LIMITATIONS.md).

| Exit code | Meaning |
|---|---|
| 0 | At least one batch is supplied, all batches are nonempty, and their declared local roots match their declared hashes and counts. `--help` also returns 0, without checking anything. |
| 1 | A supported mismatch or malformed input was observed. This takes precedence over an empty batch in the same file. |
| 2 | The file is unavailable, no batches are supplied, a batch has no event hashes, or the command has a usage error. |

For matching batches, the summary is:

```text
Batch-root check: MATCH (local hashes only; not authenticated)
```

## Limits

**This is not whole-package verification.** The script does not read `events.json`
or `anchors.json`, recompute event payload hashes, check hash chains, validate
inclusion proofs, verify signatures or external timestamp receipts, authenticate
an issuer, assess VCP conformity, or establish completeness. The generated README
and every check run state those limits. A coordinated change to hashes and their
local root can still match. A successful local root comparison does not establish
the presence, accuracy or authenticity of event or anchor files.

The historical sample under `evidence/sample_pack_2025Q1/` uses a different wrapped
JSON layout and retains its original verifier and statements. All of those files,
including its manifest, remain unchanged. The new checker does not interpret that
wrapper or upgrade the sample's historical results.

## Offline regressions

```bash
python -m unittest discover -s tests -v
```

The Python tests extract and execute the exact C# verbatim-string template. They
exercise the three-leaf odd-node case, one-leaf roots, mismatches, empty/missing
inputs, malformed JSON/UTF-8, strict number handling, count types, CLI selection,
declared limits and the preserved sample's hash-covered files.

When `dotnet` is available (or `VCP_DOTNET` points to its executable), an additional
smoke test compiles the current C# source directly in a standalone .NET 8 harness,
generates synthetic records, calls the actual export method and runs the emitted
script. It checks a normal export, its UTF-8 BOM, an export without a batch and a
temporary mismatched root. The test uses no package feeds, broker, cTrader process,
real trades, signing service or anchor requests. CI installs the .NET SDK so this
smoke test runs on both Python 3.12 and 3.14.

The standalone harness checks the current export path. It does not assess the
legacy .NET 6 NuGet packaging, cTrader integration, live trading behavior, event
hash interoperability or production readiness.
