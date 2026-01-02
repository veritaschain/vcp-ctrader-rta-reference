# VCP Event Reference

This document provides a complete reference for all VCP event types supported by this implementation.

## Event Structure

All VCP events share a common structure:

```json
{
  "Header": {
    "Version": "1.1",
    "EventID": "UUID v7",
    "EventType": "EVENT_TYPE",
    "TimestampISO": "2025-12-03T12:00:00.000000Z",
    "TimestampInt": 1733227200000000,
    "HashAlgo": "SHA256",
    "SignAlgo": "ED25519",
    "PrevHash": "optional",
    "EventHash": "computed",
    "ClockSyncStatus": "BEST_EFFORT",
    "TimestampPrecision": "MILLISECOND"
  },
  "Policy": { ... },
  "Trade": { ... },
  "Governance": { ... },
  "Risk": { ... },
  "Error": { ... }
}
```

## Trading Events

### INIT - System Initialization

Recorded when the trading system starts.

```csharp
var evt = vcp.CreateInitEvent("Bot started for USDJPY trading");
```

**Payload**:
```json
{
  "Governance": {
    "AlgorithmName": "My Trading Bot",
    "AlgorithmVersion": "1.0.0",
    "DecisionReason": "Bot started for USDJPY trading"
  }
}
```

---

### SIG - Signal Generation

Recorded when the algorithm generates a trading signal.

```csharp
await vcp.RecordSignalAsync(
    symbol: "USDJPY",
    side: "BUY",
    price: 150.123,
    confidence: 0.85,
    reason: "RSI oversold + MACD crossover"
);
```

**Payload**:
```json
{
  "Trade": {
    "Symbol": "USDJPY",
    "Side": "BUY",
    "Price": 150.123
  },
  "Governance": {
    "Confidence": 0.85,
    "DecisionReason": "RSI oversold + MACD crossover"
  }
}
```

---

### ORD - Order Submission

Recorded when an order is sent to the broker.

```csharp
await vcp.RecordOrderAsync(
    symbol: "USDJPY",
    orderId: "ORD-12345",
    side: "BUY",
    volume: 10000,
    price: 150.125,
    sl: 149.825,
    tp: 150.625
);
```

**Payload**:
```json
{
  "Trade": {
    "Symbol": "USDJPY",
    "OrderID": "ORD-12345",
    "Side": "BUY",
    "Volume": 10000,
    "Price": 150.125,
    "StopLoss": 149.825,
    "TakeProfit": 150.625
  }
}
```

---

### ACK - Order Acknowledgment

Recorded when the broker acknowledges order receipt.

```csharp
var evt = generator.CreateAckEvent(
    symbol: "USDJPY",
    orderId: "ORD-12345",
    brokerOrderId: "BRK-67890"
);
```

**Payload**:
```json
{
  "Trade": {
    "Symbol": "USDJPY",
    "OrderID": "ORD-12345",
    "BrokerOrderID": "BRK-67890"
  }
}
```

---

### EXE - Full Execution

Recorded when an order is fully executed.

```csharp
await vcp.RecordExecutionAsync(
    symbol: "USDJPY",
    orderId: "ORD-12345",
    positionId: "POS-12345",
    side: "BUY",
    volume: 10000,
    price: 150.125
);
```

**Payload**:
```json
{
  "Trade": {
    "Symbol": "USDJPY",
    "OrderID": "ORD-12345",
    "PositionID": "POS-12345",
    "Side": "BUY",
    "Volume": 10000,
    "Price": 150.125
  }
}
```

---

### PRT - Partial Fill

Recorded when an order is partially executed.

```csharp
var evt = generator.CreatePartialFillEvent(
    symbol: "USDJPY",
    orderId: "ORD-12345",
    filledVolume: 5000,
    remainingVolume: 5000,
    price: 150.125
);
```

**Payload**:
```json
{
  "Trade": {
    "Symbol": "USDJPY",
    "OrderID": "ORD-12345",
    "FilledVolume": 5000,
    "RemainingVolume": 5000,
    "Price": 150.125
  }
}
```

---

### CLS - Position Close

Recorded when a position is closed.

```csharp
await vcp.RecordCloseAsync(
    symbol: "USDJPY",
    orderId: "ORD-12345",
    positionId: "POS-12345",
    side: "SELL",
    volume: 10000,
    entryPrice: 150.125,
    closePrice: 150.625,
    profit: 5000
);
```

**Payload**:
```json
{
  "Trade": {
    "Symbol": "USDJPY",
    "OrderID": "ORD-12345",
    "PositionID": "POS-12345",
    "Side": "SELL",
    "Volume": 10000,
    "EntryPrice": 150.125,
    "ClosePrice": 150.625,
    "RealizedPnL": 5000
  }
}
```

---

### MOD - Order Modification

Recorded when an existing order or position is modified.

```csharp
var evt = generator.CreateModifyEvent(
    symbol: "USDJPY",
    orderId: "ORD-12345",
    newSL: 149.925,
    newTP: 150.725,
    reason: "Trailing stop adjustment"
);
```

**Payload**:
```json
{
  "Trade": {
    "Symbol": "USDJPY",
    "OrderID": "ORD-12345",
    "NewStopLoss": 149.925,
    "NewTakeProfit": 150.725
  },
  "Governance": {
    "DecisionReason": "Trailing stop adjustment"
  }
}
```

---

### CXL - Order Cancellation

Recorded when an order is cancelled.

```csharp
var evt = generator.CreateCancelEvent(
    symbol: "USDJPY",
    orderId: "ORD-12345",
    reason: "Market conditions changed"
);
```

**Payload**:
```json
{
  "Trade": {
    "Symbol": "USDJPY",
    "OrderID": "ORD-12345"
  },
  "Governance": {
    "DecisionReason": "Market conditions changed"
  }
}
```

---

### REJ - Order Rejection

Recorded when an order is rejected by the broker.

```csharp
var evt = generator.CreateRejectEvent(
    symbol: "USDJPY",
    orderId: "ORD-12345",
    rejectionCode: "INSUFFICIENT_MARGIN",
    rejectionMessage: "Not enough margin for this order"
);
```

**Payload**:
```json
{
  "Trade": {
    "Symbol": "USDJPY",
    "OrderID": "ORD-12345"
  },
  "Error": {
    "ErrorCode": "INSUFFICIENT_MARGIN",
    "ErrorMessage": "Not enough margin for this order",
    "Severity": "WARNING"
  }
}
```

---

## Error Events

### ERR_CONN - Connection Error

```csharp
var evt = generator.CreateConnectionErrorEvent(
    errorCode: "SOCKET_TIMEOUT",
    errorMessage: "Connection to broker timed out",
    severity: ErrorSeverity.WARNING
);
```

---

### ERR_AUTH - Authentication Error

```csharp
var evt = generator.CreateAuthErrorEvent(
    errorCode: "INVALID_TOKEN",
    errorMessage: "Session token expired",
    severity: ErrorSeverity.CRITICAL
);
```

---

### ERR_TIMEOUT - Timeout Error

```csharp
var evt = generator.CreateTimeoutErrorEvent(
    errorCode: "ORDER_TIMEOUT",
    errorMessage: "Order execution timed out after 30s",
    context: "Order ORD-12345"
);
```

---

### ERR_RISK - Risk Limit Error

```csharp
var evt = generator.CreateRiskErrorEvent(
    errorCode: "DAILY_LOSS_LIMIT",
    errorMessage: "Daily loss limit of 2% reached",
    riskMetrics: new Dictionary<string, double> {
        { "DailyPnL", -2000 },
        { "DailyLimit", -2000 },
        { "UtilizationPct", 100 }
    }
);
```

---

### ERR_SYSTEM - System Error

```csharp
var evt = generator.CreateSystemErrorEvent(
    errorCode: "DISK_FULL",
    errorMessage: "Insufficient disk space for logging",
    severity: ErrorSeverity.CRITICAL
);
```

---

### ERR_RECOVER - Recovery Event

```csharp
var evt = generator.CreateRecoveryEvent(
    recoveredFrom: "ERR_CONN",
    recoveryAction: "Reconnected to broker after 3 retries"
);
```

---

## VCP Management Events

### VCP_ANCHOR

Automatically generated when anchoring completes.

```json
{
  "Header": {
    "EventType": "VCP_ANCHOR"
  },
  "Governance": {
    "DecisionReason": "24-hour periodic anchoring",
    "AnchorTarget": "LOCAL_FILE",
    "MerkleRoot": "a1b2c3...",
    "EventCount": 500
  }
}
```

---

### VCP_BATCH

Automatically generated when a batch is completed.

```json
{
  "Header": {
    "EventType": "VCP_BATCH"
  },
  "Governance": {
    "BatchID": "batch_20251203_001",
    "MerkleRoot": "a1b2c3...",
    "EventCount": 100
  }
}
```

---

## Event Hash Calculation

Event hashes are computed according to VCP v1.1 Section 6.1.1:

```
EventHash = SHA256(Canonical(Header without EventHash) || Canonical(Payload))
```

Where `Canonical()` is RFC 8785 JSON Canonicalization (simplified).

---

## Best Practices

1. **Record all decisions**: Every signal, order, and modification should be logged
2. **Include reasons**: Always provide `DecisionReason` for governance audit
3. **Handle errors**: Log all errors with appropriate severity levels
4. **Use consistent IDs**: Link related events via OrderID and PositionID
5. **Don't skip events**: Missing events break the audit trail integrity
