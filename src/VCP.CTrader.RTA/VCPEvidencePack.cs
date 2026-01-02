// ================================================================================
// VCPEvidencePack.cs - VCP v1.1 Evidence Pack for cTrader
// ================================================================================
// VCP仕様書 v1.1準拠 - cTrader cBot統合用エビデンスパック
// Silver Tier向けリテール取引システム
// 
// 機能:
// - トレードイベントの自動記録
// - Merkleツリー構築
// - 24時間周期の外部アンカリング
// - 検証可能なエビデンス出力
// ================================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace VCP.CTrader
{
    /// <summary>
    /// VCPエビデンスパック設定
    /// </summary>
    public class VCPEvidencePackConfig
    {
        /// <summary>
        /// 基本ディレクトリ
        /// </summary>
        public string BasePath { get; set; } = "";

        /// <summary>
        /// イベント生成設定
        /// </summary>
        public VCPEventGeneratorConfig EventConfig { get; set; } = new VCPEventGeneratorConfig();

        /// <summary>
        /// アンカリング設定
        /// </summary>
        public VCPAnchorConfig AnchorConfig { get; set; } = new VCPAnchorConfig();

        /// <summary>
        /// 自動アンカリング有効
        /// </summary>
        public bool AutoAnchorEnabled { get; set; } = true;

        /// <summary>
        /// イベントログ出力有効
        /// </summary>
        public bool EventLoggingEnabled { get; set; } = true;

        /// <summary>
        /// 詳細ログ有効
        /// </summary>
        public bool VerboseLogging { get; set; } = false;

        /// <summary>
        /// バッチサイズ（この数のイベントでバッチを作成）
        /// </summary>
        public int BatchSize { get; set; } = 100;

        /// <summary>
        /// 日次レポート生成有効
        /// </summary>
        public bool DailyReportEnabled { get; set; } = true;
    }

    /// <summary>
    /// エビデンスパック統計
    /// </summary>
    public class EvidencePackStats
    {
        /// <summary>
        /// 総イベント数
        /// </summary>
        public int TotalEvents { get; set; }

        /// <summary>
        /// 総バッチ数
        /// </summary>
        public int TotalBatches { get; set; }

        /// <summary>
        /// アンカリング済みバッチ数
        /// </summary>
        public int AnchoredBatches { get; set; }

        /// <summary>
        /// 保留中バッチ数
        /// </summary>
        public int PendingBatches { get; set; }

        /// <summary>
        /// 最終イベント時刻
        /// </summary>
        public string LastEventTime { get; set; }

        /// <summary>
        /// 最終アンカー時刻
        /// </summary>
        public string LastAnchorTime { get; set; }

        /// <summary>
        /// 開始時刻
        /// </summary>
        public string StartTime { get; set; }

        /// <summary>
        /// イベントタイプ別カウント
        /// </summary>
        public Dictionary<string, int> EventTypeCounts { get; set; } = new Dictionary<string, int>();
    }

    /// <summary>
    /// VCPエビデンスパック - cTrader統合クラス
    /// </summary>
    public class VCPEvidencePack : IDisposable
    {
        private readonly VCPEvidencePackConfig _config;
        private readonly VCPEventGenerator _eventGenerator;
        private readonly VCPAnchorService _anchorService;
        private readonly List<VCPEvent> _allEvents;
        private readonly List<MerkleBatch> _batches;
        private readonly object _lockObject = new object();
        private readonly DateTimeOffset _startTime;
        private bool _initialized = false;
        private string _eventsLogPath;
        private string _batchesPath;

        /// <summary>
        /// ログイベント
        /// </summary>
        public event Action<string> OnLog;

        /// <summary>
        /// エラーイベント
        /// </summary>
        public event Action<string, Exception> OnError;

        /// <summary>
        /// アンカリング完了イベント
        /// </summary>
        public event Action<AnchorResult> OnAnchorComplete;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public VCPEvidencePack(VCPEvidencePackConfig config = null)
        {
            _config = config ?? new VCPEvidencePackConfig();
            _startTime = DateTimeOffset.UtcNow;
            _allEvents = new List<VCPEvent>();
            _batches = new List<MerkleBatch>();

            // パス初期化
            if (string.IsNullOrEmpty(_config.BasePath))
            {
                _config.BasePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "cTrader-VCP"
                );
            }

            // アンカー設定にパスを設定
            _config.AnchorConfig.LocalStoragePath = Path.Combine(_config.BasePath, "anchors");
            _config.AnchorConfig.VerboseLogging = _config.VerboseLogging;

            // コンポーネント初期化
            _eventGenerator = new VCPEventGenerator(_config.EventConfig);
            _anchorService = new VCPAnchorService(_config.AnchorConfig);

            // イベントハンドラ設定
            _anchorService.OnLog += msg => OnLog?.Invoke(msg);
        }

        /// <summary>
        /// エビデンスパックを初期化
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                // ディレクトリ作成
                Directory.CreateDirectory(_config.BasePath);
                Directory.CreateDirectory(Path.Combine(_config.BasePath, "events"));
                Directory.CreateDirectory(Path.Combine(_config.BasePath, "batches"));
                Directory.CreateDirectory(Path.Combine(_config.BasePath, "reports"));

                // ログファイルパス設定
                var dateStr = _startTime.ToString("yyyyMMdd");
                _eventsLogPath = Path.Combine(_config.BasePath, "events", $"events_{dateStr}.jsonl");
                _batchesPath = Path.Combine(_config.BasePath, "batches");

                // アンカー履歴読込
                await _anchorService.LoadAnchorHistoryAsync();

                // 初期化イベント生成
                var initEvent = _eventGenerator.CreateInitEvent("VCPエビデンスパック初期化");
                await LogEventAsync(initEvent);

                _initialized = true;
                Log($"[VCP] エビデンスパック初期化完了: {_config.BasePath}");
            }
            catch (Exception ex)
            {
                OnError?.Invoke("初期化エラー", ex);
                throw;
            }
        }

        #region イベント記録メソッド

        /// <summary>
        /// シグナルイベントを記録
        /// </summary>
        public async Task<VCPEvent> RecordSignalAsync(
            string symbol,
            string side,
            double price,
            double confidence,
            string reason,
            List<string> modelsUsed = null,
            Dictionary<string, object> inputFeatures = null)
        {
            EnsureInitialized();

            var evt = _eventGenerator.CreateSignalEvent(
                symbol, side, price, confidence, reason, modelsUsed, inputFeatures);
            
            await LogEventAsync(evt);
            await CheckAutoAnchorAsync();
            
            return evt;
        }

        /// <summary>
        /// 注文イベントを記録
        /// </summary>
        public async Task<VCPEvent> RecordOrderAsync(
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
            EnsureInitialized();

            var evt = _eventGenerator.CreateOrderEvent(
                symbol, orderId, side, volume, price, stopLoss, takeProfit, comment, spreadPips);
            
            await LogEventAsync(evt);
            await CheckAutoAnchorAsync();
            
            return evt;
        }

        /// <summary>
        /// 約定イベントを記録
        /// </summary>
        public async Task<VCPEvent> RecordExecutionAsync(
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
            EnsureInitialized();

            var evt = _eventGenerator.CreateExecuteEvent(
                symbol, orderId, positionId, side, volume, price, stopLoss, takeProfit, commission, comment);
            
            await LogEventAsync(evt);
            await CheckAutoAnchorAsync();
            
            return evt;
        }

        /// <summary>
        /// 決済イベントを記録
        /// </summary>
        public async Task<VCPEvent> RecordCloseAsync(
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
            EnsureInitialized();

            var evt = _eventGenerator.CreateCloseEvent(
                symbol, orderId, positionId, side, volume, entryPrice, closePrice, profit, swap, commission, exitReason);
            
            await LogEventAsync(evt);
            await CheckAutoAnchorAsync();
            
            return evt;
        }

        /// <summary>
        /// 変更イベントを記録
        /// </summary>
        public async Task<VCPEvent> RecordModifyAsync(
            string symbol,
            string orderId,
            string positionId,
            double? newStopLoss = null,
            double? newTakeProfit = null,
            double? newVolume = null,
            string modifyReason = null)
        {
            EnsureInitialized();

            var evt = _eventGenerator.CreateModifyEvent(
                symbol, orderId, positionId, newStopLoss, newTakeProfit, newVolume, modifyReason);
            
            await LogEventAsync(evt);
            await CheckAutoAnchorAsync();
            
            return evt;
        }

        /// <summary>
        /// キャンセルイベントを記録
        /// </summary>
        public async Task<VCPEvent> RecordCancelAsync(
            string symbol,
            string orderId,
            string cancelReason = null)
        {
            EnsureInitialized();

            var evt = _eventGenerator.CreateCancelEvent(symbol, orderId, cancelReason);
            
            await LogEventAsync(evt);
            await CheckAutoAnchorAsync();
            
            return evt;
        }

        /// <summary>
        /// 拒否イベントを記録
        /// </summary>
        public async Task<VCPEvent> RecordRejectAsync(
            string symbol,
            string orderId,
            string rejectReason,
            string errorCode = null)
        {
            EnsureInitialized();

            var evt = _eventGenerator.CreateRejectEvent(symbol, orderId, rejectReason, errorCode);
            
            await LogEventAsync(evt);
            await CheckAutoAnchorAsync();
            
            return evt;
        }

        /// <summary>
        /// エラーイベントを記録
        /// </summary>
        public async Task<VCPEvent> RecordErrorAsync(
            VCPEventType errorType,
            string errorCode,
            string errorMessage,
            ErrorSeverity severity,
            string affectedComponent,
            string recoveryAction = null)
        {
            EnsureInitialized();

            var evt = _eventGenerator.CreateErrorEvent(
                errorType, errorCode, errorMessage, severity, affectedComponent, recoveryAction);
            
            await LogEventAsync(evt);
            await CheckAutoAnchorAsync();
            
            return evt;
        }

        #endregion

        #region バッチ・アンカリング

        /// <summary>
        /// 現在のイベントからバッチを作成
        /// </summary>
        public async Task<MerkleBatch> CreateBatchAsync()
        {
            EnsureInitialized();

            List<VCPEvent> events;
            lock (_lockObject)
            {
                if (_allEvents.Count == 0)
                {
                    return null;
                }
                events = new List<VCPEvent>(_allEvents);
            }

            var batch = _anchorService.CreateAndAddBatch(events, _config.EventConfig.ConformanceTier);
            
            lock (_lockObject)
            {
                _batches.Add(batch);
            }

            // バッチ情報保存
            var batchPath = Path.Combine(_batchesPath, $"batch_{batch.BatchID.Substring(0, 8)}.json");
            var batchJson = JsonSerializer.Serialize(batch, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(batchPath, batchJson, Encoding.UTF8);

            Log($"[VCP] バッチ作成: {batch.BatchID} (イベント数: {batch.EventCount})");
            return batch;
        }

        /// <summary>
        /// 手動アンカリング実行
        /// </summary>
        public async Task<List<AnchorResult>> AnchorNowAsync()
        {
            EnsureInitialized();

            // まずバッチを作成
            if (_anchorService.PendingBatchCount == 0)
            {
                await CreateBatchAsync();
            }

            // アンカリング実行
            var results = await _anchorService.AnchorPendingBatchesAsync();

            foreach (var result in results)
            {
                OnAnchorComplete?.Invoke(result);
            }

            // 履歴保存
            await _anchorService.SaveAnchorHistoryAsync();

            return results;
        }

        /// <summary>
        /// 自動アンカリングチェック
        /// </summary>
        private async Task CheckAutoAnchorAsync()
        {
            if (!_config.AutoAnchorEnabled)
                return;

            // バッチサイズに達したらバッチ作成
            if (_allEvents.Count >= _config.BatchSize && _allEvents.Count % _config.BatchSize == 0)
            {
                await CreateBatchAsync();
            }

            // アンカリング時刻に達したらアンカリング
            if (_anchorService.IsAnchoringDue())
            {
                Log("[VCP] 自動アンカリング開始...");
                await AnchorNowAsync();
            }
        }

        #endregion

        #region 統計・レポート

        /// <summary>
        /// 統計情報を取得
        /// </summary>
        public EvidencePackStats GetStats()
        {
            lock (_lockObject)
            {
                var stats = new EvidencePackStats
                {
                    TotalEvents = _allEvents.Count,
                    TotalBatches = _batches.Count,
                    AnchoredBatches = _batches.FindAll(b => b.IsAnchored).Count,
                    PendingBatches = _anchorService.PendingBatchCount,
                    StartTime = _startTime.ToString("O"),
                    LastAnchorTime = _anchorService.LastAnchorTime != DateTimeOffset.MinValue
                        ? _anchorService.LastAnchorTime.ToString("O")
                        : "未実行"
                };

                if (_allEvents.Count > 0)
                {
                    stats.LastEventTime = _allEvents[_allEvents.Count - 1].Header.TimestampISO;
                }

                // イベントタイプ別カウント
                foreach (var evt in _allEvents)
                {
                    var typeName = evt.Header.EventType.ToString();
                    if (stats.EventTypeCounts.ContainsKey(typeName))
                        stats.EventTypeCounts[typeName]++;
                    else
                        stats.EventTypeCounts[typeName] = 1;
                }

                return stats;
            }
        }

        /// <summary>
        /// 日次レポートを生成
        /// </summary>
        public async Task<string> GenerateDailyReportAsync()
        {
            EnsureInitialized();

            var stats = GetStats();
            var reportDate = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");

            var sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine($"VCP エビデンスパック 日次レポート - {reportDate}");
            sb.AppendLine("================================================================================");
            sb.AppendLine();
            sb.AppendLine("【概要】");
            sb.AppendLine($"  開始時刻: {stats.StartTime}");
            sb.AppendLine($"  総イベント数: {stats.TotalEvents}");
            sb.AppendLine($"  総バッチ数: {stats.TotalBatches}");
            sb.AppendLine($"  アンカリング済み: {stats.AnchoredBatches}");
            sb.AppendLine($"  保留中: {stats.PendingBatches}");
            sb.AppendLine($"  最終イベント: {stats.LastEventTime}");
            sb.AppendLine($"  最終アンカー: {stats.LastAnchorTime}");
            sb.AppendLine();
            sb.AppendLine("【イベントタイプ別】");
            foreach (var kvp in stats.EventTypeCounts)
            {
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }
            sb.AppendLine();
            sb.AppendLine("【アンカー履歴】");
            foreach (var anchor in _anchorService.AnchorHistory)
            {
                sb.AppendLine($"  {anchor.AnchorID}");
                sb.AppendLine($"    時刻: {anchor.AnchorTimestamp}");
                sb.AppendLine($"    ターゲット: {anchor.AnchorTarget}");
                sb.AppendLine($"    イベント数: {anchor.EventCount}");
                sb.AppendLine($"    Merkleルート: {anchor.MerkleRoot.Substring(0, 16)}...");
            }
            sb.AppendLine();
            sb.AppendLine("================================================================================");

            var reportContent = sb.ToString();

            // レポート保存
            if (_config.DailyReportEnabled)
            {
                var reportPath = Path.Combine(_config.BasePath, "reports", $"report_{reportDate}.txt");
                await File.WriteAllTextAsync(reportPath, reportContent, Encoding.UTF8);
                Log($"[VCP] 日次レポート保存: {reportPath}");
            }

            return reportContent;
        }

        /// <summary>
        /// 検証用エビデンスパッケージをエクスポート
        /// </summary>
        public async Task<string> ExportVerificationPackageAsync(string outputPath = null)
        {
            EnsureInitialized();

            var exportDir = outputPath ?? Path.Combine(_config.BasePath, "export", $"vcp_export_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(exportDir);

            // 全イベントをエクスポート
            var eventsPath = Path.Combine(exportDir, "events.json");
            List<VCPEvent> events;
            lock (_lockObject)
            {
                events = new List<VCPEvent>(_allEvents);
            }
            var eventsJson = JsonSerializer.Serialize(events, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(eventsPath, eventsJson, Encoding.UTF8);

            // 全バッチをエクスポート
            var batchesExportPath = Path.Combine(exportDir, "batches.json");
            List<MerkleBatch> batches;
            lock (_lockObject)
            {
                batches = new List<MerkleBatch>(_batches);
            }
            var batchesJson = JsonSerializer.Serialize(batches, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(batchesExportPath, batchesJson, Encoding.UTF8);

            // アンカー履歴をエクスポート
            var anchorsPath = Path.Combine(exportDir, "anchors.json");
            var anchorsJson = JsonSerializer.Serialize(_anchorService.AnchorHistory, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(anchorsPath, anchorsJson, Encoding.UTF8);

            // 検証スクリプト生成
            var verifyScriptPath = Path.Combine(exportDir, "verify.py");
            await File.WriteAllTextAsync(verifyScriptPath, GenerateVerificationScript(), Encoding.UTF8);

            // READMEを生成
            var readmePath = Path.Combine(exportDir, "README.md");
            await File.WriteAllTextAsync(readmePath, GenerateExportReadme(), Encoding.UTF8);

            Log($"[VCP] エビデンスパッケージエクスポート完了: {exportDir}");
            return exportDir;
        }

        #endregion

        #region 内部メソッド

        /// <summary>
        /// 初期化チェック
        /// </summary>
        private void EnsureInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("VCPEvidencePackが初期化されていません。InitializeAsync()を先に呼び出してください。");
            }
        }

        /// <summary>
        /// イベントをログに記録
        /// </summary>
        private async Task LogEventAsync(VCPEvent evt)
        {
            lock (_lockObject)
            {
                _allEvents.Add(evt);
            }

            if (_config.EventLoggingEnabled)
            {
                var json = VCPEventGenerator.SerializeEvent(evt);
                await File.AppendAllTextAsync(_eventsLogPath, json + "\n", Encoding.UTF8);
            }

            Log($"[VCP] イベント記録: {evt.Header.EventType} - {evt.Header.EventID.Substring(0, 8)}");
        }

        /// <summary>
        /// ログ出力
        /// </summary>
        private void Log(string message)
        {
            if (_config.VerboseLogging)
            {
                OnLog?.Invoke(message);
            }
        }

        /// <summary>
        /// 検証スクリプト生成
        /// </summary>
        private string GenerateVerificationScript()
        {
            return @"#!/usr/bin/env python3
# VCP v1.1 検証スクリプト
# このスクリプトでエビデンスパッケージの整合性を検証できます

import json
import hashlib
from pathlib import Path

def compute_merkle_hash(data: bytes, is_leaf: bool = True) -> bytes:
    prefix = b'\x00' if is_leaf else b'\x01'
    return hashlib.sha256(prefix + data).digest()

def verify_merkle_root(event_hashes: list, expected_root: str) -> bool:
    if not event_hashes:
        return False
    
    # リーフノード作成
    current_level = [compute_merkle_hash(bytes.fromhex(h), is_leaf=True) for h in event_hashes]
    
    # ツリー構築
    while len(current_level) > 1:
        next_level = []
        for i in range(0, len(current_level), 2):
            left = current_level[i]
            right = current_level[i + 1] if i + 1 < len(current_level) else left
            combined = left + right
            next_level.append(compute_merkle_hash(combined, is_leaf=False))
        current_level = next_level
    
    computed_root = current_level[0].hex()
    return computed_root == expected_root.lower()

def main():
    base_path = Path(__file__).parent
    
    # バッチ読込
    with open(base_path / 'batches.json', 'r', encoding='utf-8') as f:
        batches = json.load(f)
    
    print('=== VCP エビデンスパッケージ検証 ===\n')
    
    for batch in batches:
        batch_id = batch['BatchID'][:8]
        merkle_root = batch['MerkleRoot']
        event_hashes = batch['EventHashes']
        
        is_valid = verify_merkle_root(event_hashes, merkle_root)
        status = '✓ 検証成功' if is_valid else '✗ 検証失敗'
        
        print(f'バッチ {batch_id}: {status}')
        print(f'  イベント数: {batch[""EventCount""]}')
        print(f'  Merkleルート: {merkle_root[:16]}...')
        print()
    
    print('検証完了')

if __name__ == '__main__':
    main()
";
        }

        /// <summary>
        /// エクスポートREADME生成
        /// </summary>
        private string GenerateExportReadme()
        {
            return $@"# VCP v1.1 エビデンスパッケージ

## 概要
このパッケージにはVCP (VeritasChain Protocol) v1.1準拠のトレードエビデンスが含まれています。

## ファイル構成
- `events.json` - 全トレードイベント
- `batches.json` - Merkleバッチ情報（包含証明付き）
- `anchors.json` - 外部アンカーレコード
- `verify.py` - Python検証スクリプト

## 検証方法
```bash
python verify.py
```

## VCP準拠情報
- プロトコルバージョン: 1.1
- コンプライアンス階層: {_config.EventConfig.ConformanceTier}
- ポリシーID: {_config.EventConfig.PolicyID}
- エクスポート日時: {DateTimeOffset.UtcNow:O}

## 参照
- VCP仕様書: https://veritaschain.org
- VeritasChain Standards Organization (VSO)
";
        }

        public void Dispose()
        {
            _anchorService?.Dispose();
        }

        #endregion
    }
}
