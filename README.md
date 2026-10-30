> **Status correction — 25 October 2026**
>
> - This is first-party reference code by VeritasChain Co., Ltd., which operates the VeritasChain Standards Organization (VSO). It targets VCP v1.1 (Silver tier). VCP v1.1 has no tagged release and is superseded by VCP v1.2 (Released, tag `v1.2.0`).
> - Conformance to any VCP tier, or to VAP, has not been independently established. There are zero external implementations of VAP or any of its profiles, including VCP.
> - Not certified. VSO does not issue certifications itself. The certification scheme requires a Conformity Assessment Body (CAB) independent of the scheme owner; the only registered CAB, VeritasChain Co., Ltd., is not independent (VAP v1.2 ERRATA E-2026-10-21).
> - Known limitations of the sample evidence pack: its only anchor (`anchors.json`) uses `AnchorTarget: "LOCAL_FILE"` with `Signature: null` and `VerificationStatus: "UNVERIFIED"`. A local file is not an external anchor, so the pack does not meet the external-anchoring requirement (VAP v1.2 INT-006; VCP Silver). The events carry no signatures. The Merkle builder (`VCPMerkleTree.cs`, `verify.py`) duplicates the last node at odd-sized levels; it uses the RFC 6962 hash prefixes but not the RFC 6962 tree algorithm.
> - All files under `evidence/sample_pack_2025Q1/` are left unchanged. The five files listed in its `hash_manifest.json`, including its README and `verify.py`, are hash-covered; statements in the preserved files predate this correction.

# VCP cTrader RTA Reference Implementation

[![License: CC BY 4.0](https://img.shields.io/badge/License-CC%20BY%204.0-lightgrey.svg)](https://creativecommons.org/licenses/by/4.0/)
[![.NET](https://img.shields.io/badge/.NET-6.0%2B-blue.svg)](https://dotnet.microsoft.com/)
[![VCP Version](https://img.shields.io/badge/VCP-v1.1-green.svg)](https://github.com/veritaschain/vcp-spec)
[![Status](https://img.shields.io/badge/Status-Non--certified-orange.svg)](#sample-evidence-pack)

> **This is a non-certified reference implementation and evidence pack for independent verification. No endorsement or regulatory approval is implied.**

Reference implementation demonstrating **VeritasChain Protocol (VCP) v1.1** audit trails for cTrader-based trading workflows. This repository contains a verifiable evidence pack generated from a live cTrader environment, enabling independent third-party verification of order and execution logs.

## Features

- 🔐 **VCP v1.1 Silver target** - Conformance not established (see the status correction above)
- 🌳 **Merkle Trees (RFC 6962 hash prefixes)** - Domain-separated leaf/node hashing; odd-sized levels duplicate the last node (not the RFC 6962 tree algorithm)
- ⚓ **Anchoring** - Local file (not an external anchor), OpenTimestamps (Bitcoin), FreeTSA code paths; the sample pack uses only a local file
- 📝 **Complete Event Types** - SIG, ORD, ACK, EXE, PRT, CLS, MOD, CXL, REJ, ERR_*
- 🔄 **Automatic Batching** - Configurable batch sizes with automatic Merkle root generation
- 📊 **Verification Export** - Self-contained verification packages with Python scripts
- 🚀 **High Performance** - >1K events/second, <1s latency

## Quick Start

### Installation

Clone this repository and add the project reference to your cBot solution:

```bash
git clone https://github.com/veritaschain/vcp-ctrader-rta-reference.git
```

Or copy the source files from `src/VCP.CTrader.RTA/` into your cBot project.

### Basic Usage

```csharp
using VCP.CTrader;

// Configure VCP Evidence Pack
var config = new VCPEvidencePackConfig
{
    BasePath = @"C:\VCPData",
    EventConfig = new VCPEventGeneratorConfig
    {
        PolicyID = "com.yourcompany:ctrader-bot-v1",
        Issuer = "Your Organization",
        AlgorithmName = "My Trading Bot",
        AlgorithmVersion = "1.0.0",
        ConformanceTier = ConformanceTier.SILVER
    },
    AutoAnchorEnabled = true
};

// Initialize
var vcp = new VCPEvidencePack(config);
await vcp.InitializeAsync();

// Record trading events
await vcp.RecordSignalAsync("USDJPY", "BUY", 150.123, 0.85, "Technical signal");
await vcp.RecordOrderAsync("USDJPY", "ORD-001", "BUY", 10000, 150.125, 149.825, 150.625);
await vcp.RecordExecutionAsync("USDJPY", "ORD-001", "POS-001", "BUY", 10000, 150.125);
await vcp.RecordCloseAsync("USDJPY", "ORD-001", "POS-001", "SELL", 10000, 150.125, 150.625, 5000);

// Export verification package
await vcp.ExportVerificationPackageAsync("audit_2025Q1");
```

### cBot Integration

See [examples/SampleVCPBot.cs](examples/SampleVCPBot.cs) for a complete cBot integration example.

```csharp
public class MyVCPBot : Robot
{
    private VCPEvidencePack _vcp;

    protected override void OnStart()
    {
        _vcp = new VCPEvidencePack(new VCPEvidencePackConfig { ... });
        _vcp.InitializeAsync().Wait();
    }

    protected override void OnPositionsOpened(PositionOpenedEventArgs args)
    {
        _vcp.RecordExecutionAsync(...).Wait();
    }

    protected override void OnPositionsClosed(PositionClosedEventArgs args)
    {
        _vcp.RecordCloseAsync(...).Wait();
    }

    protected override void OnStop()
    {
        _vcp.AnchorNowAsync().Wait();
        _vcp.GenerateDailyReportAsync().Wait();
    }
}
```

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        VCPEvidencePack                          │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐          │
│  │    Events    │  │    Merkle    │  │   Anchoring  │          │
│  │  Generator   │──│     Tree     │──│   Service    │          │
│  └──────────────┘  └──────────────┘  └──────────────┘          │
│         │                 │                 │                   │
│         ▼                 ▼                 ▼                   │
│  ┌──────────────────────────────────────────────────┐          │
│  │              Persistent Storage                   │          │
│  │  • events_YYYYMMDD.jsonl                         │          │
│  │  • batches/batch_<id>.json                       │          │
│  │  • anchors/anchor_<id>.json                      │          │
│  └──────────────────────────────────────────────────┘          │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
              ┌───────────────────────────────┐
              │     External Anchors          │
              │  • Local File (not external)  │
              │  • OpenTimestamps (Bitcoin)   │
              │  • FreeTSA (RFC 3161)         │
              │  • Custom HTTP endpoint       │
              └───────────────────────────────┘
```

## Project Structure

```
vcp-ctrader-rta-reference/
├── README.md                          # This file
├── LICENSE                            # CC BY 4.0
├── CONTRIBUTING.md                    # Contribution guidelines
├── .gitignore                         # Git ignore rules
├── VCP.CTrader.RTA.sln               # Solution file
├── src/
│   └── VCP.CTrader.RTA/
│       ├── VCP.CTrader.RTA.csproj    # Project file
│       ├── VCPCore.cs                 # Core data structures
│       ├── VCPMerkleTree.cs          # Merkle tree implementation
│       ├── VCPEventGenerator.cs      # Event generation
│       ├── VCPAnchor.cs              # External anchoring
│       └── VCPEvidencePack.cs        # Main integration class
├── examples/
│   └── SampleVCPBot.cs               # cBot integration example
├── evidence/
│   └── sample_pack_2025Q1/           # Verifiable evidence pack
│       ├── events.json               # Trade events (masked)
│       ├── batches.json              # Merkle batches
│       ├── anchors.json              # Anchor records
│       ├── hash_manifest.json        # SHA-256 integrity manifest
│       ├── verify.py                 # Verification script
│       ├── README.md                 # Evidence pack guide
│       └── certificates/             # Event certificates
│           └── event_certificate_*.json
├── docs/
│   ├── ARCHITECTURE.md               # Architecture details
│   ├── EVENTS.md                     # Event type reference
│   ├── PLATFORM.md                   # cTrader integration guide
│   └── VERIFICATION.md               # Verification guide
└── .github/
    └── workflows/
        └── dotnet.yml                # CI: Build + Evidence Verification
```

## Sample Evidence Pack

This repository includes a **verifiable evidence pack** generated from a cTrader environment with masked identifiers:

```bash
cd evidence/sample_pack_2025Q1

# Quick integrity check via hash manifest
cat hash_manifest.json | python3 -c "import sys,json; [print(f'{k}: {v[\"sha256\"][:16]}...') for k,v in json.load(sys.stdin)['files'].items()]"

# Full verification
python verify.py
```

The sample contains 21 VCP events (per `events.json` and `hash_manifest.json`) demonstrating:
- Trading signals and executions
- Order lifecycle (SIG → ORD → ACK → EXE → CLS)
- Error handling and recovery
- Merkle batching and anchoring

**Integrity Verification**: The `hash_manifest.json` provides SHA-256 hashes for all evidence files, enabling tamper detection at a glance.

> ⚠️ **Note**: Personal identifiers (organization, account, broker, algorithm names) have been masked. Currency symbols shown as `XXXYYY`.

## Silver Tier Target

This implementation targets **VCP Silver Tier** for retail trading systems. Conformance has not been established. A local file anchor is not an external anchor under VAP v1.2 INT-006, and the Merkle construction duplicates odd nodes rather than following the RFC 6962 tree algorithm:

| Requirement | Implementation |
|-------------|----------------|
| Time Sync | BEST_EFFORT (system clock) |
| Timestamp Precision | MILLISECOND |
| Anchor Frequency | 24 hours |
| Serialization | JSON |
| Throughput | >1K events/second |
| Latency | <1 second |
| Hash Algorithm | SHA-256 |
| Merkle Construction | RFC 6962 hash prefixes; odd-node duplication (not the RFC 6962 tree algorithm) |

## Event Types

### Trading Events
- `INIT` - System initialization
- `SIG` - Signal generation
- `ORD` - Order submission
- `ACK` - Order acknowledgment
- `EXE` - Full execution
- `PRT` - Partial fill
- `CLS` - Position close
- `MOD` - Order modification
- `CXL` - Order cancellation
- `REJ` - Order rejection

### Error Events
- `ERR_CONN` - Connection error
- `ERR_AUTH` - Authentication error
- `ERR_TIMEOUT` - Timeout
- `ERR_REJECT` - Rejection
- `ERR_RISK` - Risk limit breach

## Documentation

- [Architecture Guide](docs/ARCHITECTURE.md) - Detailed system architecture
- [Event Reference](docs/EVENTS.md) - Complete event type documentation
- [Platform Guide](docs/PLATFORM.md) - cTrader integration & MT5 comparison
- [Verification Guide](docs/VERIFICATION.md) - How to verify audit trails

## Requirements

- .NET 6.0 or later
- cTrader 4.x (for cBot integration)

### Optional Dependencies

For production Ed25519 signatures:
- [Chaos.NaCl](https://github.com/CodesInChaos/Chaos.NaCl)
- [BouncyCastle](https://www.bouncycastle.org/csharp/)

## Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## Related Projects

- [VCP Specification](https://github.com/veritaschain/vcp-spec) - Official VCP specification
- [VCP RTA Reference](https://github.com/veritaschain/vcp-rta-reference) - MetaTrader 5 implementation

## License

This project is licensed under [CC BY 4.0](LICENSE) - Creative Commons Attribution 4.0 International.

## Acknowledgments

- VeritasChain Standards Organization (VSO) for the VCP specification
- RFC 6962 (Certificate Transparency) for Merkle tree construction standards
- RFC 9562 for UUID v7 specification

---

**Disclaimer**: This is a reference implementation for educational and development purposes. Production deployments should undergo thorough security review and testing.

---

### Data Masking

The sample evidence pack uses the following masking conventions:

| Original Data | Masked Value | Purpose |
|---------------|--------------|---------|
| Organization name | `[MASKED_ORGANIZATION]` | Privacy protection |
| Account ID | `[MASKED_ACCOUNT_ID]` | Account privacy |
| Broker name | `[MASKED_BROKER]` | Broker confidentiality |
| Algorithm name | `[MASKED_ALGORITHM]` | IP protection |
| Technical indicators | `[MASKED_INDICATOR]` | Strategy confidentiality |
| Currency pair | `XXXYYY` | Symbol anonymization |

**Important**: Despite masking, all SHA-256 hashes are **real cryptographic computations** 
and remain fully verifiable. The masking affects only human-readable metadata, 
not the cryptographic integrity of the audit trail.
