# VCP cTrader RTA Reference Implementation

[![License: CC BY 4.0](https://img.shields.io/badge/License-CC%20BY%204.0-lightgrey.svg)](https://creativecommons.org/licenses/by/4.0/)
[![.NET](https://img.shields.io/badge/.NET-6.0%2B-blue.svg)](https://dotnet.microsoft.com/)
[![VCP Version](https://img.shields.io/badge/VCP-v1.1-green.svg)](https://github.com/veritaschain/vcp-specification)
[![Evidence Verification](https://img.shields.io/badge/Evidence-Verified-brightgreen.svg)](#sample-evidence-pack)

> **This is a non-certified reference implementation and evidence pack for independent verification. No endorsement or regulatory approval is implied.**

Reference implementation demonstrating **VeritasChain Protocol (VCP) v1.1** audit trails for cTrader-based trading workflows. This repository contains a verifiable evidence pack generated from a live cTrader environment, enabling independent third-party verification of order and execution logs.

## Features

- 🔐 **VCP v1.1 Compliant** - Full specification compliance for Silver Tier
- 🌳 **RFC 6962 Merkle Trees** - Domain-separated hash construction with second preimage attack protection
- ⚓ **External Anchoring** - Local file, OpenTimestamps (Bitcoin), FreeTSA support
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
              │  • Local File (required)      │
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

The sample contains 24 VCP events demonstrating:
- Trading signals and executions
- Order lifecycle (SIG → ORD → ACK → EXE → CLS)
- Error handling and recovery
- Merkle batching and anchoring

**Integrity Verification**: The `hash_manifest.json` provides SHA-256 hashes for all evidence files, enabling tamper detection at a glance.

> ⚠️ **Note**: Personal identifiers (organization, account, broker, algorithm names) have been masked. Currency symbols shown as `XXXYYY`.

## Silver Tier Compliance

This implementation targets **VCP Silver Tier** for retail trading systems:

| Requirement | Implementation |
|-------------|----------------|
| Time Sync | BEST_EFFORT (system clock) |
| Timestamp Precision | MILLISECOND |
| Anchor Frequency | 24 hours |
| Serialization | JSON |
| Throughput | >1K events/second |
| Latency | <1 second |
| Hash Algorithm | SHA-256 |
| Merkle Construction | RFC 6962 (domain-separated) |

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

- [VCP Specification](https://github.com/veritaschain/vcp-specification) - Official VCP specification
- [VCP MT5 Reference](https://github.com/veritaschain/vcp-mt5-rta-reference) - MetaTrader 5 implementation

## License

This project is licensed under [CC BY 4.0](LICENSE) - Creative Commons Attribution 4.0 International.

## Acknowledgments

- VeritasChain Standards Organization (VSO) for the VCP specification
- RFC 6962 (Certificate Transparency) for Merkle tree construction standards
- RFC 9562 for UUID v7 specification

---

**Disclaimer**: This is a reference implementation for educational and development purposes. Production deployments should undergo thorough security review and testing.
