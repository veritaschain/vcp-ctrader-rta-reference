// ================================================================================
// SampleVCPBot.cs - VCP v1.1対応 サンプルcBot
// ================================================================================
// VCPエビデンスパックをcTrader cBotに統合する例
// 実際の使用時はcAlgo.APIを参照してください
// ================================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// cAlgo.API名前空間のモック（実際の実装ではcAlgo.APIを使用）
namespace cAlgo.API
{
    // プレースホルダー型（実際のcTraderでは不要）
    public class Robot
    {
        public virtual void OnStart() { }
        public virtual void OnTick() { }
        public virtual void OnBar() { }
        public virtual void OnStop() { }
        public virtual void OnPositionsOpened(Position position) { }
        public virtual void OnPositionsClosed(Position position) { }
        protected void Print(string message) => Console.WriteLine(message);
        protected TradeResult ExecuteMarketOrder(TradeType type, string symbol, double volume, string label, double? sl, double? tp) => new TradeResult();
        protected TradeResult ModifyPosition(Position position, double? sl, double? tp) => new TradeResult();
        protected TradeResult ClosePosition(Position position) => new TradeResult();
        public Symbol Symbol { get; set; }
        public Account Account { get; set; }
        public Positions Positions { get; set; }
        public History History { get; set; }
    }
    public class Symbol { public string Name { get; set; } = "USDJPY"; public double Bid { get; set; } = 150.0; public double Ask { get; set; } = 150.01; public double Spread { get; set; } = 0.01; public double PipSize { get; set; } = 0.01; }
    public class Account { public string Number { get; set; } = "12345"; public string BrokerName { get; set; } = "cTrader Broker"; public double Balance { get; set; } = 100000; public double Equity { get; set; } = 100000; }
    public class Positions { public Position[] ToArray() => Array.Empty<Position>(); }
    public class History { public HistoricalTrade[] ToArray() => Array.Empty<HistoricalTrade>(); }
    public class Position { public int Id { get; set; } public string Label { get; set; } public string SymbolName { get; set; } public TradeType TradeType { get; set; } public double VolumeInUnits { get; set; } public double EntryPrice { get; set; } public double CurrentPrice { get; set; } public double? StopLoss { get; set; } public double? TakeProfit { get; set; } public double NetProfit { get; set; } public double Swap { get; set; } public double Commissions { get; set; } }
    public class HistoricalTrade { public int PositionId { get; set; } public double ClosingPrice { get; set; } public double NetProfit { get; set; } }
    public class TradeResult { public bool IsSuccessful { get; set; } = true; public Position Position { get; set; } public Error Error { get; set; } }
    public class Error { public string Message { get; set; } }
    public enum TradeType { Buy, Sell }
    public class IndicatorAttribute : Attribute { public bool IsOverlay { get; set; } public string TimeZone { get; set; } public string AccessRights { get; set; } }
    public class ParameterAttribute : Attribute { public string Name { get; set; } public object DefaultValue { get; set; } }
}

namespace VCP.CTrader
{
    using cAlgo.API;

    /// <summary>
    /// VCP v1.1対応サンプルcBot
    /// トレードイベントを自動的にVCPエビデンスとして記録
    /// </summary>
    [Indicator(IsOverlay = false, TimeZone = "UTC", AccessRights = "FullAccess")]
    public class SampleVCPBot : Robot
    {
        #region パラメータ

        [Parameter("ポリシーID", DefaultValue = "com.example.trading:ctrader-bot-v1")]
        public string PolicyId { get; set; } = "com.example.trading:ctrader-bot-v1";

        [Parameter("組織名", DefaultValue = "Example Trading")]
        public string IssuerName { get; set; } = "Example Trading";

        [Parameter("アルゴリズム名", DefaultValue = "Sample VCP Bot")]
        public string AlgorithmName { get; set; } = "Sample VCP Bot";

        [Parameter("バージョン", DefaultValue = "1.0.0")]
        public string AlgorithmVersion { get; set; } = "1.0.0";

        [Parameter("自動アンカリング", DefaultValue = true)]
        public bool AutoAnchor { get; set; } = true;

        [Parameter("詳細ログ", DefaultValue = true)]
        public bool VerboseLog { get; set; } = true;

        [Parameter("ロットサイズ", DefaultValue = 0.01)]
        public double LotSize { get; set; } = 0.01;

        [Parameter("ストップロス (pips)", DefaultValue = 30)]
        public double StopLossPips { get; set; } = 30;

        [Parameter("テイクプロフィット (pips)", DefaultValue = 60)]
        public double TakeProfitPips { get; set; } = 60;

        #endregion

        #region プライベートフィールド

        private VCPEvidencePack _vcpPack;
        private bool _isInitialized = false;

        #endregion

        #region cBot ライフサイクル

        /// <summary>
        /// cBot起動時
        /// </summary>
        public override void OnStart()
        {
            Print("=== VCP v1.1 対応 cBot 起動 ===");

            try
            {
                // VCPエビデンスパック設定
                var config = new VCPEvidencePackConfig
                {
                    EventConfig = new VCPEventGeneratorConfig
                    {
                        PolicyID = PolicyId,
                        Issuer = IssuerName,
                        AlgorithmName = AlgorithmName,
                        AlgorithmVersion = AlgorithmVersion,
                        BrokerName = Account?.BrokerName ?? "cTrader",
                        AccountID = Account?.Number ?? "Unknown",
                        ConformanceTier = ConformanceTier.SILVER,
                        UseHashChain = false // v1.1ではオプション
                    },
                    AnchorConfig = new VCPAnchorConfig
                    {
                        PrimaryTarget = AnchorTargetType.LOCAL_FILE,
                        AnchorIntervalHours = 24, // Silver Tier: 24時間
                        VerboseLogging = VerboseLog
                    },
                    AutoAnchorEnabled = AutoAnchor,
                    EventLoggingEnabled = true,
                    VerboseLogging = VerboseLog,
                    BatchSize = 50,
                    DailyReportEnabled = true
                };

                // エビデンスパック初期化
                _vcpPack = new VCPEvidencePack(config);
                _vcpPack.OnLog += msg => Print(msg);
                _vcpPack.OnError += (msg, ex) => Print($"[VCP ERROR] {msg}: {ex.Message}");
                _vcpPack.OnAnchorComplete += result =>
                {
                    if (result.Success)
                        Print($"[VCP] アンカリング成功: {result.AnchorRecord.AnchorID}");
                    else
                        Print($"[VCP] アンカリング失敗: {result.ErrorMessage}");
                };

                // 非同期初期化（同期的に待機）
                Task.Run(async () => await _vcpPack.InitializeAsync()).Wait();
                _isInitialized = true;

                Print("[VCP] エビデンスパック初期化完了");
            }
            catch (Exception ex)
            {
                Print($"[VCP ERROR] 初期化失敗: {ex.Message}");
                _isInitialized = false;
            }
        }

        /// <summary>
        /// ティック毎
        /// </summary>
        public override void OnTick()
        {
            // ここにトレードロジックを実装
            // 例: シグナル検出 → 注文実行
        }

        /// <summary>
        /// バー毎
        /// </summary>
        public override void OnBar()
        {
            // ここにバーベースのロジックを実装
            // 例: 1時間足のクローズでシグナルチェック
        }

        /// <summary>
        /// cBot停止時
        /// </summary>
        public override void OnStop()
        {
            Print("=== VCP v1.1 対応 cBot 停止 ===");

            if (_isInitialized && _vcpPack != null)
            {
                try
                {
                    // 最終アンカリング実行
                    Print("[VCP] 最終アンカリング実行中...");
                    Task.Run(async () => await _vcpPack.AnchorNowAsync()).Wait();

                    // 日次レポート生成
                    Print("[VCP] 日次レポート生成中...");
                    var report = Task.Run(async () => await _vcpPack.GenerateDailyReportAsync()).Result;
                    Print(report);

                    // 統計出力
                    var stats = _vcpPack.GetStats();
                    Print($"[VCP] 総イベント数: {stats.TotalEvents}");
                    Print($"[VCP] 総バッチ数: {stats.TotalBatches}");
                    Print($"[VCP] アンカリング済み: {stats.AnchoredBatches}");

                    _vcpPack.Dispose();
                }
                catch (Exception ex)
                {
                    Print($"[VCP ERROR] 停止処理エラー: {ex.Message}");
                }
            }
        }

        #endregion

        #region ポジションイベント

        /// <summary>
        /// ポジションオープン時
        /// </summary>
        public override void OnPositionsOpened(Position position)
        {
            if (!_isInitialized) return;

            try
            {
                Task.Run(async () =>
                {
                    await _vcpPack.RecordExecutionAsync(
                        symbol: position.SymbolName,
                        orderId: position.Id.ToString(),
                        positionId: position.Id.ToString(),
                        side: position.TradeType == TradeType.Buy ? "BUY" : "SELL",
                        volume: position.VolumeInUnits,
                        price: position.EntryPrice,
                        stopLoss: position.StopLoss ?? 0,
                        takeProfit: position.TakeProfit ?? 0,
                        commission: position.Commissions,
                        comment: position.Label
                    );
                }).Wait();

                Print($"[VCP] ポジションオープン記録: {position.Id}");
            }
            catch (Exception ex)
            {
                Print($"[VCP ERROR] ポジションオープン記録失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// ポジションクローズ時
        /// </summary>
        public override void OnPositionsClosed(Position position)
        {
            if (!_isInitialized) return;

            try
            {
                Task.Run(async () =>
                {
                    await _vcpPack.RecordCloseAsync(
                        symbol: position.SymbolName,
                        orderId: position.Id.ToString(),
                        positionId: position.Id.ToString(),
                        side: position.TradeType == TradeType.Buy ? "BUY" : "SELL",
                        volume: position.VolumeInUnits,
                        entryPrice: position.EntryPrice,
                        closePrice: position.CurrentPrice,
                        profit: position.NetProfit,
                        swap: position.Swap,
                        commission: position.Commissions,
                        exitReason: "ポジション決済"
                    );
                }).Wait();

                Print($"[VCP] ポジションクローズ記録: {position.Id}, 損益: {position.NetProfit:F2}");
            }
            catch (Exception ex)
            {
                Print($"[VCP ERROR] ポジションクローズ記録失敗: {ex.Message}");
            }
        }

        #endregion

        #region トレード実行メソッド

        /// <summary>
        /// シグナルを記録してエントリー
        /// </summary>
        protected async Task<TradeResult> ExecuteWithVCPAsync(
            TradeType tradeType,
            double confidence,
            string reason,
            List<string> modelsUsed = null)
        {
            if (!_isInitialized)
            {
                Print("[VCP WARNING] VCP未初期化でトレード実行");
                return ExecuteMarketOrder(tradeType, Symbol.Name, LotSize, AlgorithmName, null, null);
            }

            var side = tradeType == TradeType.Buy ? "BUY" : "SELL";
            var price = tradeType == TradeType.Buy ? Symbol.Ask : Symbol.Bid;

            // 1. シグナル記録
            await _vcpPack.RecordSignalAsync(
                symbol: Symbol.Name,
                side: side,
                price: price,
                confidence: confidence,
                reason: reason,
                modelsUsed: modelsUsed
            );

            // 2. SL/TP計算
            var pipSize = Symbol.PipSize;
            double? sl = tradeType == TradeType.Buy
                ? price - StopLossPips * pipSize
                : price + StopLossPips * pipSize;
            double? tp = tradeType == TradeType.Buy
                ? price + TakeProfitPips * pipSize
                : price - TakeProfitPips * pipSize;

            // 3. 注文記録
            var orderId = Guid.NewGuid().ToString("N").Substring(0, 8);
            await _vcpPack.RecordOrderAsync(
                symbol: Symbol.Name,
                orderId: orderId,
                side: side,
                volume: LotSize,
                price: price,
                stopLoss: sl.Value,
                takeProfit: tp.Value,
                comment: reason,
                spreadPips: Symbol.Spread / pipSize
            );

            // 4. 実際の注文実行
            var result = ExecuteMarketOrder(tradeType, Symbol.Name, LotSize, AlgorithmName, sl, tp);

            if (result.IsSuccessful)
            {
                Print($"[VCP] 注文成功: {side} {LotSize} lots @ {price}");
            }
            else
            {
                // 拒否記録
                await _vcpPack.RecordRejectAsync(
                    symbol: Symbol.Name,
                    orderId: orderId,
                    rejectReason: result.Error?.Message ?? "注文失敗"
                );
                Print($"[VCP] 注文失敗: {result.Error?.Message}");
            }

            return result;
        }

        /// <summary>
        /// ポジション変更をVCPに記録
        /// </summary>
        protected async Task<TradeResult> ModifyWithVCPAsync(
            Position position,
            double? newStopLoss,
            double? newTakeProfit,
            string modifyReason)
        {
            if (!_isInitialized)
            {
                return ModifyPosition(position, newStopLoss, newTakeProfit);
            }

            // 変更記録
            await _vcpPack.RecordModifyAsync(
                symbol: position.SymbolName,
                orderId: position.Id.ToString(),
                positionId: position.Id.ToString(),
                newStopLoss: newStopLoss,
                newTakeProfit: newTakeProfit,
                modifyReason: modifyReason
            );

            // 実際の変更実行
            var result = ModifyPosition(position, newStopLoss, newTakeProfit);

            if (result.IsSuccessful)
            {
                Print($"[VCP] ポジション変更成功: {position.Id}");
            }
            else
            {
                await _vcpPack.RecordErrorAsync(
                    errorType: VCPEventType.ERR_REJECT,
                    errorCode: "MODIFY_FAILED",
                    errorMessage: result.Error?.Message ?? "変更失敗",
                    severity: ErrorSeverity.WARNING,
                    affectedComponent: "position-management"
                );
            }

            return result;
        }

        /// <summary>
        /// ポジション決済をVCPに記録
        /// </summary>
        protected async Task<TradeResult> CloseWithVCPAsync(
            Position position,
            string closeReason)
        {
            if (!_isInitialized)
            {
                return ClosePosition(position);
            }

            var result = ClosePosition(position);

            if (result.IsSuccessful)
            {
                await _vcpPack.RecordCloseAsync(
                    symbol: position.SymbolName,
                    orderId: position.Id.ToString(),
                    positionId: position.Id.ToString(),
                    side: position.TradeType == TradeType.Buy ? "BUY" : "SELL",
                    volume: position.VolumeInUnits,
                    entryPrice: position.EntryPrice,
                    closePrice: position.CurrentPrice,
                    profit: position.NetProfit,
                    swap: position.Swap,
                    commission: position.Commissions,
                    exitReason: closeReason
                );
                Print($"[VCP] 決済成功: {position.Id}, 損益: {position.NetProfit:F2}");
            }

            return result;
        }

        #endregion

        #region ユーティリティ

        /// <summary>
        /// 手動アンカリング実行
        /// </summary>
        public async Task ManualAnchorAsync()
        {
            if (!_isInitialized)
            {
                Print("[VCP WARNING] VCP未初期化");
                return;
            }

            Print("[VCP] 手動アンカリング実行中...");
            var results = await _vcpPack.AnchorNowAsync();

            foreach (var result in results)
            {
                if (result.Success)
                    Print($"[VCP] アンカリング成功: {result.AnchorRecord.AnchorID}");
                else
                    Print($"[VCP] アンカリング失敗: {result.ErrorMessage}");
            }
        }

        /// <summary>
        /// エビデンスパッケージをエクスポート
        /// </summary>
        public async Task<string> ExportEvidenceAsync()
        {
            if (!_isInitialized)
            {
                Print("[VCP WARNING] VCP未初期化");
                return null;
            }

            var exportPath = await _vcpPack.ExportVerificationPackageAsync();
            Print($"[VCP] エビデンスエクスポート完了: {exportPath}");
            return exportPath;
        }

        /// <summary>
        /// 統計情報を表示
        /// </summary>
        public void ShowStats()
        {
            if (!_isInitialized)
            {
                Print("[VCP WARNING] VCP未初期化");
                return;
            }

            var stats = _vcpPack.GetStats();
            Print("=== VCP 統計情報 ===");
            Print($"総イベント数: {stats.TotalEvents}");
            Print($"総バッチ数: {stats.TotalBatches}");
            Print($"アンカリング済み: {stats.AnchoredBatches}");
            Print($"保留中: {stats.PendingBatches}");
            Print($"開始時刻: {stats.StartTime}");
            Print($"最終イベント: {stats.LastEventTime}");
            Print($"最終アンカー: {stats.LastAnchorTime}");
        }

        #endregion
    }
}
