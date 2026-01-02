# Platform Integration Guide

## Overview

This document describes how VCP events are captured from the cTrader platform and compares with MetaTrader 5 (MT5) integration.

## cTrader Event Sources

### Primary: cBot API (Automate API)

This reference implementation uses the **cTrader Automate cBot API** for event capture.

| Event Type | cBot API Source | Trigger |
|------------|-----------------|---------|
| INIT | `OnStart()` | Bot initialization |
| SIG | Custom logic | Algorithm signal generation |
| ORD | `ExecuteMarketOrder()` / `PlaceLimitOrder()` | Order submission |
| ACK | `TradeResult` | Order accepted by broker |
| EXE | `OnPositionsOpened` | Position opened |
| PRT | `OnPendingOrdersFilled` | Partial fill (pending orders) |
| CLS | `OnPositionsClosed` | Position closed |
| MOD | `ModifyPosition()` | SL/TP modification |
| CXL | `CancelPendingOrder()` | Order cancellation |
| REJ | `TradeResult.Error` | Order rejection |

### Alternative Sources

| Source | Use Case | Availability |
|--------|----------|--------------|
| **cTrader Open API** | External applications, mobile | Requires OAuth |
| **FIX Protocol** | Institutional, DMA | Broker-dependent |
| **cTrader Copy** | Copy trading platforms | Limited access |

## Comparison: cTrader vs MT5

### Signal Generation (SIG)

| Aspect | cTrader | MT5 |
|--------|---------|-----|
| Location | cBot `OnBar()` / `OnTick()` | EA `OnTick()` / External Python |
| AI Integration | External API call | Python via file I/O |
| Latency | ~10-50ms | ~50-200ms (file-based) |

### Order/Execution Flow (ORD → EXE)

| Event | cTrader | MT5 |
|-------|---------|-----|
| Order Submission | `ExecuteMarketOrder()` sync | `OrderSend()` sync |
| Acknowledgment | `TradeResult` immediate | `OrderSend()` return code |
| Execution | `OnPositionsOpened` callback | Poll `PositionSelect()` |
| Position ID | Native `Position.Id` | `POSITION_TICKET` |

### Key Differences

```
cTrader Flow:
  SIG → ORD → [ACK] → EXE (callback-driven)
       └── TradeResult includes ACK

MT5 Flow:
  SIG → ORD → EXE (poll-driven)
       └── OrderSend() blocks until fill
```

## Platform Constraints

### Account Types

| Type | VCP Support | Notes |
|------|-------------|-------|
| Demo | ✅ Full | Recommended for testing |
| Live | ✅ Full | Production use |
| Contest | ⚠️ Limited | May have API restrictions |

### Broker Requirements

- **cTrader Version**: 4.x or later
- **Account Access**: Full API access enabled
- **Permissions**: `AccessRights = FullAccess` for cBot

### Time Precision

| Platform | Native Precision | VCP Output |
|----------|------------------|------------|
| cTrader | Milliseconds | MILLISECOND |
| MT5 | Seconds (extended: ms) | MILLISECOND |
| Server Time | Broker-dependent | UTC normalized |

### Rate Limits

| Operation | cTrader | MT5 |
|-----------|---------|-----|
| Orders/sec | ~10 (broker-dependent) | ~5-10 |
| API calls/sec | ~100 | ~50 (file I/O bound) |
| VCP events/sec | >1000 | >1000 |

## Integration Checklist

### cTrader Setup

```
☐ cTrader 4.x installed
☐ cBot project created
☐ AccessRights = FullAccess
☐ VCP.CTrader.RTA referenced
☐ Output directory configured
```

### MT5 Setup (for comparison)

```
☐ MT5 terminal installed
☐ Python environment configured
☐ File I/O paths set (MQL5/Files)
☐ EA and Python script synchronized
☐ Timer-based polling configured
```

## Why cTrader for VCP Reference?

1. **Event-driven architecture**: Native callbacks vs polling
2. **Modern API**: Clean C# interface vs MQL5
3. **Better timestamps**: Native millisecond precision
4. **Async support**: Non-blocking event recording
5. **Cross-platform**: Windows/Mac/Linux support

## Migration Notes

### From MT5 to cTrader

- Replace file-based signal I/O with direct API calls
- Convert MQL5 indicators to cTrader Indicators
- Adapt position management (ticket → position ID)
- Update timezone handling (MT5 broker time → UTC)

### Using Both Platforms

For multi-platform deployments:
- Use consistent VCP event schema
- Normalize timestamps to UTC
- Map platform-specific IDs to common format
- Aggregate evidence packs by trading day

---

*See also: [ARCHITECTURE.md](ARCHITECTURE.md) for system design details*
