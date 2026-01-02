// ================================================================================
// VCPEventGenerator.cs - VCP v1.1 Event Generator for cTrader
// ================================================================================
// cTraderのトレードイベントをVCP形式に変換
// Silver Tier向けリテール取引システム用
// ================================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace VCP.CTrader
{
    /// <summary>
    /// VCPイベント生成設定
    /// </summary>
    public class VCPEventGeneratorConfig
    {
        /// <summary>
        /// ポリシーID (形式: reverse_domain:local_id)
        /// 例: "com.example.trading:ctrader-algo-v1"
        /// </summary>
        public string PolicyID { get; set; } = "local:ctrader:default-policy";

        /// <summary>
        /// 発行者名
        /// </summary>
        public string Issuer { get; set; } = "cTrader VCP Plugin";

        /// <summary>
        /// ポリシーURI
        /// </summary>
        public string PolicyURI { get; set; } = "";

        /// <summary>
        /// コンプライアンス階層
        /// </summary>
        public ConformanceTier ConformanceTier { get; set; } = ConformanceTier.SILVER;

        /// <summary>
        /// ハッシュチェーン使用フラグ（OPTIONAL in v1.1）
        /// </summary>
        public bool UseHashChain { get; set; } = false;

        /// <summary>
        /// アルゴリズム名
        /// </summary>
        public string AlgorithmName { get; set; } = "cTrader Bot";

        /// <summary>
        /// アルゴリズムバージョン
        /// </summary>
        public string AlgorithmVersion { get; set; } = "1.0.0";

        /// <summary>
        /// ブローカー名
        /// </summary>
        public string BrokerName { get; set; } = "";

        /// <summary>
        /// アカウントID
        /// </summary>
        public string AccountID { get; set; } = "";
    }

    /// <summary>
    /// cTrader向けVCPイベント生成クラス
    /// </summary>
    public class VCPEventGenerator
    {
        private readonly VCPEventGeneratorConfig _config;
        private readonly PolicyIdentification _policyId;
        private string _prevHash = null;
        private readonly List<VCPEvent> _eventBuffer;
        private readonly object _lockObject = new object();

        /// <summary>
        /// 生成イベント数
        /// </summary>
        public int EventCount => _eventBuffer.Count;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="config">設定</param>
        public VCPEventGenerator(VCPEventGeneratorConfig config = null)
        {
            _config = config ?? new VCPEventGeneratorConfig();
            _eventBuffer = new List<VCPEvent>();

            // ポリシー識別を初期化
            _policyId = new PolicyIdentification
            {
                Version = "1.1",
                PolicyID = _config.PolicyID,
                ConformanceTier = _config.ConformanceTier,
                RegistrationPolicy = new RegistrationPolicy
                {
                    Issuer = _config.Issuer,
                    PolicyURI = _config.PolicyURI,
                    EffectiveDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                },
                VerificationDepth = new VerificationDepth
                {
                    HashChainValidation = _config.UseHashChain,
                    MerkleProofRequired = true,
                    ExternalAnchorRequired = true
                }
            };
        }

        /// <summary>
        /// 設定を更新
        /// </summary>
        public void UpdateConfig(Action<VCPEventGeneratorConfig> updateAction)
        {
            lock (_lockObject)
            {
                updateAction(_config);
                
                // ポリシー識別を再構築
                _policyId.PolicyID = _config.PolicyID;
                _policyId.ConformanceTier = _config.ConformanceTier;
                _policyId.RegistrationPolicy.Issuer = _config.Issuer;
                _policyId.RegistrationPolicy.PolicyURI = _config.PolicyURI;
                _policyId.VerificationDepth.HashChainValidation = _config.UseHashChain;
            }
        }

        #region イベント生成メソッド

        /// <summary>
        /// システム初期化イベント (INIT)
        /// </summary>
        public VCPEvent CreateInitEvent(string comment = null)
        {
            var evt = CreateBaseEvent(VCPEventType.INIT);
            
            evt.Governance = new GovernancePayload
            {
                AlgorithmName = _config.AlgorithmName,
                AlgorithmVersion = _config.AlgorithmVersion,
                DecisionReason = comment ?? "システム初期化"
            };

            FinalizeEvent(evt);
            return evt;
        }

        /// <summary>
        /// シグナル生成イベント (SIG)
        /// </summary>
        public VCPEvent CreateSignalEvent(
            string symbol,
            string side,
            double price,
            double confidence,
            string reason,
            List<string> modelsUsed = null,
            Dictionary<string, object> inputFeatures = null)
        {
            var evt = CreateBaseEvent(VCPEventType.SIG);

            evt.Trade = new TradePayload
            {
                Symbol = symbol,
                Side = side,
                Price = price,
                Broker = _config.BrokerName,
                AccountID = _config.AccountID
            };

            evt.Governance = new GovernancePayload
            {
                AlgorithmName = _config.AlgorithmName,
                AlgorithmVersion = _config.AlgorithmVersion,
                ConfidenceScore = confidence,
                DecisionReason = reason,
                ModelsUsed = modelsUsed,
                InputFeatures = inputFeatures
            };

            FinalizeEvent(evt);
            return evt;
        }

        /// <summary>
        /// 注文送信イベント (ORD)
        /// </summary>
        public VCPEvent CreateOrderEvent(
            string symbol,
            string orderId,
            string side,
            double volume,
            double price,
            double stopLoss = 0,
            double takeProfit = 0,
            string comment = null,
            double spreadPips = 0)
        {
            var evt = CreateBaseEvent(VCPEventType.ORD);

            evt.Trade = new TradePayload
            {
                Symbol = symbol,
                OrderID = orderId,
                Side = side,
                Volume = volume,
                Price = price,
                StopLoss = stopLoss,
                TakeProfit = takeProfit,
                Comment = comment,
                SpreadPips = spreadPips,
                Broker = _config.BrokerName,
                AccountID = _config.AccountID
            };

            FinalizeEvent(evt);
            return evt;
        }

        /// <summary>
        /// 注文受理イベント (ACK)
        /// </summary>
        public VCPEvent CreateAckEvent(
            string symbol,
            string orderId,
            string positionId,
            string side,
            double volume,
            double price)
        {
            var evt = CreateBaseEvent(VCPEventType.ACK);

            evt.Trade = new TradePayload
            {
                Symbol = symbol,
                OrderID = orderId,
                PositionID = positionId,
                Side = side,
                Volume = volume,
                Price = price,
                Broker = _config.BrokerName,
                AccountID = _config.AccountID
            };

            FinalizeEvent(evt);
            return evt;
        }

        /// <summary>
        /// 約定イベント (EXE)
        /// </summary>
        public VCPEvent CreateExecuteEvent(
            string symbol,
            string orderId,
            string positionId,
            string side,
            double volume,
            double price,
            double stopLoss = 0,
            double takeProfit = 0,
            double commission = 0,
            string comment = null)
        {
            var evt = CreateBaseEvent(VCPEventType.EXE);

            evt.Trade = new TradePayload
            {
                Symbol = symbol,
                OrderID = orderId,
                PositionID = positionId,
                Side = side,
                Volume = volume,
                Price = price,
                StopLoss = stopLoss,
                TakeProfit = takeProfit,
                Commission = commission,
                Comment = comment,
                Broker = _config.BrokerName,
                AccountID = _config.AccountID
            };

            FinalizeEvent(evt);
            return evt;
        }

        /// <summary>
        /// 部分約定イベント (PRT)
        /// </summary>
        public VCPEvent CreatePartialFillEvent(
            string symbol,
            string orderId,
            string positionId,
            string side,
            double filledVolume,
            double remainingVolume,
            double price)
        {
            var evt = CreateBaseEvent(VCPEventType.PRT);

            evt.Trade = new TradePayload
            {
                Symbol = symbol,
                OrderID = orderId,
                PositionID = positionId,
                Side = side,
                Volume = filledVolume,
                Price = price,
                Broker = _config.BrokerName,
                AccountID = _config.AccountID
            };

            evt.Extensions = new Dictionary<string, object>
            {
                { "RemainingVolume", remainingVolume }
            };

            FinalizeEvent(evt);
            return evt;
        }

        /// <summary>
        /// 決済イベント (CLS)
        /// </summary>
        public VCPEvent CreateCloseEvent(
            string symbol,
            string orderId,
            string positionId,
            string side,
            double volume,
            double entryPrice,
            double closePrice,
            double profit,
            double swap = 0,
            double commission = 0,
            string exitReason = null)
        {
            var evt = CreateBaseEvent(VCPEventType.CLS);

            evt.Trade = new TradePayload
            {
                Symbol = symbol,
                OrderID = orderId,
                PositionID = positionId,
                Side = side,
                Volume = volume,
                Price = closePrice,
                Profit = profit,
                Swap = swap,
                Commission = commission,
                Broker = _config.BrokerName,
                AccountID = _config.AccountID
            };

            evt.Extensions = new Dictionary<string, object>
            {
                { "EntryPrice", entryPrice },
                { "ExitReason", exitReason ?? "不明" }
            };

            FinalizeEvent(evt);
            return evt;
        }

        /// <summary>
        /// 変更イベント (MOD)
        /// </summary>
        public VCPEvent CreateModifyEvent(
            string symbol,
            string orderId,
            string positionId,
            double? newStopLoss = null,
            double? newTakeProfit = null,
            double? newVolume = null,
            string modifyReason = null)
        {
            var evt = CreateBaseEvent(VCPEventType.MOD);

            evt.Trade = new TradePayload
            {
                Symbol = symbol,
                OrderID = orderId,
                PositionID = positionId,
                Broker = _config.BrokerName,
                AccountID = _config.AccountID
            };

            if (newStopLoss.HasValue)
                evt.Trade.StopLoss = newStopLoss.Value;
            if (newTakeProfit.HasValue)
                evt.Trade.TakeProfit = newTakeProfit.Value;
            if (newVolume.HasValue)
                evt.Trade.Volume = newVolume.Value;

            evt.Extensions = new Dictionary<string, object>
            {
                { "ModifyReason", modifyReason ?? "手動変更" }
            };

            FinalizeEvent(evt);
            return evt;
        }

        /// <summary>
        /// キャンセルイベント (CXL)
        /// </summary>
        public VCPEvent CreateCancelEvent(
            string symbol,
            string orderId,
            string cancelReason = null)
        {
            var evt = CreateBaseEvent(VCPEventType.CXL);

            evt.Trade = new TradePayload
            {
                Symbol = symbol,
                OrderID = orderId,
                Broker = _config.BrokerName,
                AccountID = _config.AccountID
            };

            evt.Extensions = new Dictionary<string, object>
            {
                { "CancelReason", cancelReason ?? "ユーザーキャンセル" }
            };

            FinalizeEvent(evt);
            return evt;
        }

        /// <summary>
        /// 拒否イベント (REJ)
        /// </summary>
        public VCPEvent CreateRejectEvent(
            string symbol,
            string orderId,
            string rejectReason,
            string errorCode = null)
        {
            var evt = CreateBaseEvent(VCPEventType.REJ);

            evt.Trade = new TradePayload
            {
                Symbol = symbol,
                OrderID = orderId,
                Broker = _config.BrokerName,
                AccountID = _config.AccountID
            };

            evt.ErrorDetails = new ErrorDetails
            {
                ErrorCode = errorCode ?? "REJECT",
                ErrorMessage = rejectReason,
                Severity = ErrorSeverity.WARNING,
                AffectedComponent = "order-execution"
            };

            FinalizeEvent(evt);
            return evt;
        }

        /// <summary>
        /// エラーイベント生成
        /// </summary>
        public VCPEvent CreateErrorEvent(
            VCPEventType errorType,
            string errorCode,
            string errorMessage,
            ErrorSeverity severity,
            string affectedComponent,
            string recoveryAction = null,
            string correlatedEventId = null)
        {
            var evt = CreateBaseEvent(errorType);

            evt.ErrorDetails = new ErrorDetails
            {
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                Severity = severity,
                AffectedComponent = affectedComponent,
                RecoveryAction = recoveryAction,
                CorrelatedEventID = correlatedEventId
            };

            FinalizeEvent(evt);
            return evt;
        }

        /// <summary>
        /// リスク情報付きイベント生成
        /// </summary>
        public VCPEvent CreateRiskEvent(
            VCPEventType eventType,
            TradePayload trade,
            double positionSize,
            double riskPercentage,
            double maxDrawdown,
            double currentDrawdown,
            double dailyLossLimit,
            double dailyPnL)
        {
            var evt = CreateBaseEvent(eventType);

            evt.Trade = trade;
            evt.Risk = new RiskPayload
            {
                PositionSize = positionSize,
                RiskPercentage = riskPercentage,
                MaxDrawdown = maxDrawdown,
                CurrentDrawdown = currentDrawdown,
                DailyLossLimit = dailyLossLimit,
                DailyPnL = dailyPnL
            };

            FinalizeEvent(evt);
            return evt;
        }

        #endregion

        #region バッファ管理

        /// <summary>
        /// イベントをバッファに追加
        /// </summary>
        public void AddEventToBuffer(VCPEvent evt)
        {
            lock (_lockObject)
            {
                _eventBuffer.Add(evt);
            }
        }

        /// <summary>
        /// バッファをクリアしてイベントを返す
        /// </summary>
        public List<VCPEvent> FlushBuffer()
        {
            lock (_lockObject)
            {
                var events = new List<VCPEvent>(_eventBuffer);
                _eventBuffer.Clear();
                return events;
            }
        }

        /// <summary>
        /// バッファ内イベントを取得（クリアしない）
        /// </summary>
        public List<VCPEvent> GetBufferedEvents()
        {
            lock (_lockObject)
            {
                return new List<VCPEvent>(_eventBuffer);
            }
        }

        /// <summary>
        /// 前回ハッシュをリセット
        /// </summary>
        public void ResetHashChain()
        {
            lock (_lockObject)
            {
                _prevHash = null;
            }
        }

        #endregion

        #region 内部メソッド

        /// <summary>
        /// 基本イベントを作成
        /// </summary>
        private VCPEvent CreateBaseEvent(VCPEventType eventType)
        {
            var now = DateTimeOffset.UtcNow;

            var evt = new VCPEvent
            {
                Header = new VCPHeader
                {
                    Version = "1.1",
                    EventID = VCPUtility.GenerateEventID(),
                    EventType = eventType,
                    TimestampISO = VCPUtility.GetISOTimestamp(now),
                    TimestampInt = VCPUtility.GetUnixMicroseconds(now),
                    HashAlgo = HashAlgo.SHA256,
                    SignAlgo = SignAlgo.ED25519,
                    ClockSyncStatus = ClockSyncStatus.BEST_EFFORT,
                    TimestampPrecision = TimestampPrecision.MILLISECOND
                },
                PolicyIdentification = _policyId
            };

            // ハッシュチェーンが有効な場合
            if (_config.UseHashChain)
            {
                evt.Header.PrevHash = _prevHash ?? new string('0', 64);
            }

            return evt;
        }

        /// <summary>
        /// イベントを最終化（ハッシュ計算等）
        /// </summary>
        private void FinalizeEvent(VCPEvent evt)
        {
            // ペイロードを構築
            var payload = new Dictionary<string, object>();
            
            if (evt.Trade != null)
                payload["Trade"] = evt.Trade;
            if (evt.Governance != null)
                payload["Governance"] = evt.Governance;
            if (evt.Risk != null)
                payload["Risk"] = evt.Risk;
            if (evt.ErrorDetails != null)
                payload["ErrorDetails"] = evt.ErrorDetails;
            if (evt.Extensions != null)
                payload["Extensions"] = evt.Extensions;
            if (evt.PolicyIdentification != null)
                payload["PolicyIdentification"] = evt.PolicyIdentification;

            // イベントハッシュを計算
            evt.Header.EventHash = VCPUtility.ComputeEventHash(evt.Header, payload);

            // ハッシュチェーンが有効な場合は前回ハッシュを更新
            if (_config.UseHashChain)
            {
                lock (_lockObject)
                {
                    _prevHash = evt.Header.EventHash;
                }
            }

            // バッファに追加
            AddEventToBuffer(evt);
        }

        #endregion

        #region シリアライズ

        /// <summary>
        /// イベントをJSON文字列に変換
        /// </summary>
        public static string SerializeEvent(VCPEvent evt, bool indented = false)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = indented,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            return JsonSerializer.Serialize(evt, options);
        }

        /// <summary>
        /// JSON文字列からイベントを復元
        /// </summary>
        public static VCPEvent DeserializeEvent(string json)
        {
            return JsonSerializer.Deserialize<VCPEvent>(json);
        }

        #endregion
    }
}
