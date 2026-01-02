# VCP cTrader RTA Architecture

## Overview

This document describes the architecture of the VCP cTrader RTA Reference Implementation.

## Component Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                           cTrader cBot                               │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                     Trading Logic                            │   │
│  │   OnStart() → OnTick() → OnBar() → OnStop()                 │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                              │                                       │
│                              ▼                                       │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                   VCPEvidencePack                            │   │
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐           │   │
│  │  │   Events    │ │   Merkle    │ │   Anchor    │           │   │
│  │  │  Generator  │ │    Tree     │ │   Service   │           │   │
│  │  └─────────────┘ └─────────────┘ └─────────────┘           │   │
│  └─────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      Persistent Storage                              │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                 │
│  │   Events    │  │   Batches   │  │   Anchors   │                 │
│  │  (JSONL)    │  │   (JSON)    │  │   (JSON)    │                 │
│  └─────────────┘  └─────────────┘  └─────────────┘                 │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     External Anchoring                               │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                 │
│  │   Local     │  │  OpenTime-  │  │   FreeTSA   │                 │
│  │   File      │  │   stamps    │  │  RFC 3161   │                 │
│  └─────────────┘  └─────────────┘  └─────────────┘                 │
└─────────────────────────────────────────────────────────────────────┘
```

## Core Components

### 1. VCPCore

**Purpose**: Core data structures and utilities

**Key Classes**:
- `VCPHeader` - Event header with timestamps, hashes
- `TradePayload` - Trade-specific event data
- `GovernancePayload` - Decision audit data
- `VCPUtility` - UUID v7, SHA-256, JSON canonicalization

**Standards Compliance**:
- UUID v7 (RFC 9562)
- JSON Canonicalization (RFC 8785 simplified)
- SHA-256 hashing

### 2. VCPMerkleTree

**Purpose**: RFC 6962-compliant Merkle tree construction

**Features**:
- Domain-separated hashing (0x00 for leaves, 0x01 for internal nodes)
- Second preimage attack protection
- Inclusion proof generation and verification

**Algorithm**:
```
Leaf Hash:    H(0x00 || data)
Internal:     H(0x01 || left || right)
```

### 3. VCPEventGenerator

**Purpose**: Generate VCP-compliant events from cTrader operations

**Event Types**:
| Type | Description |
|------|-------------|
| INIT | System initialization |
| SIG | Signal generation |
| ORD | Order submission |
| ACK | Order acknowledgment |
| EXE | Full execution |
| PRT | Partial fill |
| CLS | Position close |
| MOD | Order modification |
| CXL | Cancellation |
| REJ | Rejection |
| ERR_* | Error events |

### 4. VCPAnchor

**Purpose**: External timestamping for immutability

**Supported Targets**:
1. **LOCAL_FILE** - Offline anchoring (always available)
2. **OPENTIMESTAMPS** - Bitcoin blockchain anchoring
3. **FREE_TSA** - RFC 3161 timestamp authority
4. **CUSTOM_HTTP** - Custom endpoint

**Anchor Frequency**: 24 hours (Silver Tier)

### 5. VCPEvidencePack

**Purpose**: Integration layer combining all components

**Features**:
- Automatic event logging
- Configurable batch sizes
- Periodic anchoring
- Verification package export

## Data Flow

### Event Recording Flow

```
1. Trading Event (e.g., order execution)
       │
       ▼
2. VCPEventGenerator.CreateEvent()
       │
       ▼
3. Compute EventHash (SHA-256)
       │
       ▼
4. Append to Event Buffer
       │
       ▼
5. Write to JSONL log
       │
       ▼
6. If buffer full → Create Batch
       │
       ▼
7. Build Merkle Tree
       │
       ▼
8. Generate Merkle Root
```

### Anchoring Flow

```
1. Timer triggers (24h interval) or Manual call
       │
       ▼
2. Collect pending batches
       │
       ▼
3. Submit Merkle Root to anchor target
       │
       ▼
4. Receive anchor proof
       │
       ▼
5. Store AnchorRecord
       │
       ▼
6. Update batch status
```

## Storage Format

### Events (JSONL)

```json
{"Header":{"Version":"1.1","EventID":"...","EventType":"EXE",...},"Trade":{...}}
{"Header":{"Version":"1.1","EventID":"...","EventType":"CLS",...},"Trade":{...}}
```

### Batch (JSON)

```json
{
  "BatchID": "batch_20251203_001",
  "MerkleRoot": "a1b2c3...",
  "EventCount": 100,
  "StartTime": "2025-12-03T00:00:00Z",
  "EndTime": "2025-12-03T23:59:59Z",
  "EventHashes": ["hash1", "hash2", ...],
  "IsAnchored": true,
  "AnchorRecord": {...}
}
```

### Anchor Record (JSON)

```json
{
  "AnchorID": "anchor_20251203_001",
  "MerkleRoot": "a1b2c3...",
  "AnchorTimestamp": "2025-12-03T12:00:00Z",
  "AnchorTarget": "LOCAL_FILE",
  "AnchorProof": "...",
  "EventCount": 500,
  "ConformanceTier": "SILVER"
}
```

## Security Considerations

### Hash Chain (Optional)

Events can optionally be linked via PrevHash:
```
Event[n].PrevHash = Event[n-1].EventHash
```

### Tamper Detection

1. Event hash includes all payload data
2. Merkle tree enables efficient proof of inclusion
3. External anchors provide timestamp immutability

### Attack Mitigations

| Attack | Mitigation |
|--------|------------|
| Second preimage | RFC 6962 domain separation |
| Replay | UUID v7 timestamps |
| Backdating | External anchoring |
| Data modification | SHA-256 hash chain |

## Performance

### Silver Tier Requirements

| Metric | Target | Implementation |
|--------|--------|----------------|
| Throughput | >1K evt/s | Achieved via buffering |
| Latency | <1s | Async operations |
| Storage | Efficient | JSONL compression |
| Memory | Bounded | Batch flushing |

### Optimization Strategies

1. **Buffered Writing**: Events accumulated before disk write
2. **Async Anchoring**: Non-blocking external calls
3. **Lazy Merkle Construction**: Only on batch completion
4. **Incremental Logging**: JSONL append-only format
