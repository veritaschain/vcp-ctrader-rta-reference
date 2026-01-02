// ================================================================================
// VCPAnchor.cs - VCP v1.1 External Anchoring Service
// ================================================================================
// VCP仕様書 v1.1 Section 6.3.3準拠 - 外部アンカリング
// Silver Tier: 24時間周期のアンカリング
// サポート対象: OpenTimestamps, FreeTSA, ローカルファイル
// ================================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VCP.CTrader
{
    /// <summary>
    /// アンカーターゲットタイプ
    /// </summary>
    public enum AnchorTargetType
    {
        /// <summary>
        /// ローカルファイル保存（オフラインアンカー）
        /// </summary>
        LOCAL_FILE,

        /// <summary>
        /// OpenTimestamps（Bitcoin）
        /// </summary>
        OPENTIMESTAMPS,

        /// <summary>
        /// FreeTSA（無料RFC 3161タイムスタンプ）
        /// </summary>
        FREE_TSA,

        /// <summary>
        /// カスタムHTTPエンドポイント
        /// </summary>
        CUSTOM_HTTP
    }

    /// <summary>
    /// アンカリング設定
    /// </summary>
    public class VCPAnchorConfig
    {
        /// <summary>
        /// プライマリアンカーターゲット
        /// </summary>
        public AnchorTargetType PrimaryTarget { get; set; } = AnchorTargetType.LOCAL_FILE;

        /// <summary>
        /// フォールバックアンカーターゲット
        /// </summary>
        public AnchorTargetType? FallbackTarget { get; set; } = null;

        /// <summary>
        /// アンカリング間隔（時間）- Silver Tier: 24時間
        /// </summary>
        public int AnchorIntervalHours { get; set; } = 24;

        /// <summary>
        /// ローカル保存ディレクトリ
        /// </summary>
        public string LocalStoragePath { get; set; } = "";

        /// <summary>
        /// カスタムHTTPエンドポイントURL
        /// </summary>
        public string CustomHttpEndpoint { get; set; } = "";

        /// <summary>
        /// カスタムHTTPヘッダー
        /// </summary>
        public Dictionary<string, string> CustomHttpHeaders { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// リトライ回数
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// リトライ待機時間（秒）
        /// </summary>
        public int RetryDelaySeconds { get; set; } = 5;

        /// <summary>
        /// 詳細ログ有効
        /// </summary>
        public bool VerboseLogging { get; set; } = false;
    }

    /// <summary>
    /// アンカリング結果
    /// </summary>
    public class AnchorResult
    {
        /// <summary>
        /// 成功フラグ
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// アンカーレコード
        /// </summary>
        public AnchorRecord AnchorRecord { get; set; }

        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 使用したターゲット
        /// </summary>
        public AnchorTargetType TargetUsed { get; set; }

        /// <summary>
        /// 処理時間（ミリ秒）
        /// </summary>
        public long ProcessingTimeMs { get; set; }
    }

    /// <summary>
    /// VCP外部アンカリングサービス
    /// </summary>
    public class VCPAnchorService : IDisposable
    {
        private readonly VCPAnchorConfig _config;
        private readonly HttpClient _httpClient;
        private readonly List<MerkleBatch> _pendingBatches;
        private readonly List<AnchorRecord> _anchorHistory;
        private readonly object _lockObject = new object();
        private DateTimeOffset _lastAnchorTime;

        /// <summary>
        /// アンカー履歴
        /// </summary>
        public IReadOnlyList<AnchorRecord> AnchorHistory => _anchorHistory.AsReadOnly();

        /// <summary>
        /// 保留バッチ数
        /// </summary>
        public int PendingBatchCount => _pendingBatches.Count;

        /// <summary>
        /// 最終アンカー時刻
        /// </summary>
        public DateTimeOffset LastAnchorTime => _lastAnchorTime;

        /// <summary>
        /// ログイベント
        /// </summary>
        public event Action<string> OnLog;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public VCPAnchorService(VCPAnchorConfig config = null)
        {
            _config = config ?? new VCPAnchorConfig();
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
            _pendingBatches = new List<MerkleBatch>();
            _anchorHistory = new List<AnchorRecord>();
            _lastAnchorTime = DateTimeOffset.MinValue;

            // ローカルストレージパスの初期化
            if (string.IsNullOrEmpty(_config.LocalStoragePath))
            {
                _config.LocalStoragePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "cTrader-VCP",
                    "anchors"
                );
            }
        }

        /// <summary>
        /// Merkleバッチをアンカリング待ちに追加
        /// </summary>
        public void AddPendingBatch(MerkleBatch batch)
        {
            lock (_lockObject)
            {
                _pendingBatches.Add(batch);
                Log($"[VCPAnchor] バッチ追加: {batch.BatchID} (イベント数: {batch.EventCount})");
            }
        }

        /// <summary>
        /// イベントリストからバッチを作成して追加
        /// </summary>
        public MerkleBatch CreateAndAddBatch(List<VCPEvent> events, ConformanceTier tier = ConformanceTier.SILVER)
        {
            if (events == null || events.Count == 0)
            {
                throw new ArgumentException("イベントリストが空です");
            }

            // Merkleツリーを構築
            var tree = new VCPMerkleTree();
            tree.BuildFromEvents(events);

            // バッチを作成
            var batch = new MerkleBatch
            {
                BatchID = VCPUtility.GenerateEventID(),
                MerkleRoot = tree.GetMerkleRoot(),
                EventCount = events.Count,
                StartTime = events[0].Header.TimestampISO,
                EndTime = events[events.Count - 1].Header.TimestampISO,
                ConformanceTier = tier,
                EventHashes = events.ConvertAll(e => e.Header.EventHash),
                InclusionProofs = new List<MerkleInclusionProof>(),
                IsAnchored = false
            };

            // 各イベントの包含証明を生成
            for (int i = 0; i < events.Count; i++)
            {
                var proof = tree.GenerateInclusionProofByIndex(i, events[i].Header.EventID);
                batch.InclusionProofs.Add(proof);
            }

            AddPendingBatch(batch);
            return batch;
        }

        /// <summary>
        /// アンカリングが必要かチェック
        /// </summary>
        public bool IsAnchoringDue()
        {
            if (_pendingBatches.Count == 0)
                return false;

            var timeSinceLastAnchor = DateTimeOffset.UtcNow - _lastAnchorTime;
            return timeSinceLastAnchor.TotalHours >= _config.AnchorIntervalHours;
        }

        /// <summary>
        /// 保留中バッチをアンカリング
        /// </summary>
        public async Task<List<AnchorResult>> AnchorPendingBatchesAsync()
        {
            List<MerkleBatch> batchesToAnchor;
            lock (_lockObject)
            {
                if (_pendingBatches.Count == 0)
                {
                    Log("[VCPAnchor] アンカリング対象バッチなし");
                    return new List<AnchorResult>();
                }
                batchesToAnchor = new List<MerkleBatch>(_pendingBatches);
            }

            var results = new List<AnchorResult>();
            
            foreach (var batch in batchesToAnchor)
            {
                var result = await AnchorBatchAsync(batch);
                results.Add(result);

                if (result.Success)
                {
                    lock (_lockObject)
                    {
                        batch.IsAnchored = true;
                        batch.AnchorRecord = result.AnchorRecord;
                        _pendingBatches.Remove(batch);
                        _anchorHistory.Add(result.AnchorRecord);
                    }
                }
            }

            _lastAnchorTime = DateTimeOffset.UtcNow;
            return results;
        }

        /// <summary>
        /// 単一バッチをアンカリング
        /// </summary>
        public async Task<AnchorResult> AnchorBatchAsync(MerkleBatch batch)
        {
            var startTime = DateTimeOffset.UtcNow;
            Log($"[VCPAnchor] アンカリング開始: {batch.BatchID}");

            // プライマリターゲットを試行
            var result = await TryAnchorAsync(batch, _config.PrimaryTarget);

            // 失敗時はフォールバックを試行
            if (!result.Success && _config.FallbackTarget.HasValue)
            {
                Log($"[VCPAnchor] プライマリ失敗、フォールバック試行: {_config.FallbackTarget}");
                result = await TryAnchorAsync(batch, _config.FallbackTarget.Value);
            }

            result.ProcessingTimeMs = (long)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
            
            if (result.Success)
            {
                Log($"[VCPAnchor] アンカリング成功: {batch.BatchID} ({result.ProcessingTimeMs}ms)");
            }
            else
            {
                Log($"[VCPAnchor] アンカリング失敗: {batch.BatchID} - {result.ErrorMessage}");
            }

            return result;
        }

        /// <summary>
        /// 指定ターゲットへのアンカリングを試行
        /// </summary>
        private async Task<AnchorResult> TryAnchorAsync(MerkleBatch batch, AnchorTargetType target)
        {
            int retries = 0;
            while (retries < _config.MaxRetries)
            {
                try
                {
                    switch (target)
                    {
                        case AnchorTargetType.LOCAL_FILE:
                            return await AnchorToLocalFileAsync(batch);

                        case AnchorTargetType.OPENTIMESTAMPS:
                            return await AnchorToOpenTimestampsAsync(batch);

                        case AnchorTargetType.FREE_TSA:
                            return await AnchorToFreeTSAAsync(batch);

                        case AnchorTargetType.CUSTOM_HTTP:
                            return await AnchorToCustomHttpAsync(batch);

                        default:
                            return new AnchorResult
                            {
                                Success = false,
                                ErrorMessage = $"未対応のアンカーターゲット: {target}"
                            };
                    }
                }
                catch (Exception ex)
                {
                    retries++;
                    if (retries < _config.MaxRetries)
                    {
                        Log($"[VCPAnchor] リトライ {retries}/{_config.MaxRetries}: {ex.Message}");
                        await Task.Delay(_config.RetryDelaySeconds * 1000);
                    }
                    else
                    {
                        return new AnchorResult
                        {
                            Success = false,
                            ErrorMessage = $"アンカリング失敗 (リトライ{retries}回): {ex.Message}",
                            TargetUsed = target
                        };
                    }
                }
            }

            return new AnchorResult
            {
                Success = false,
                ErrorMessage = "最大リトライ回数超過"
            };
        }

        #region アンカリング実装

        /// <summary>
        /// ローカルファイルへのアンカリング
        /// Silver Tier向け最小実装
        /// </summary>
        private async Task<AnchorResult> AnchorToLocalFileAsync(MerkleBatch batch)
        {
            // ディレクトリ作成
            Directory.CreateDirectory(_config.LocalStoragePath);

            var now = DateTimeOffset.UtcNow;
            var anchorId = $"anchor_{now:yyyyMMdd_HHmmss}_{batch.BatchID.Substring(0, 8)}";

            // アンカーレコード作成
            var anchorRecord = new AnchorRecord
            {
                AnchorID = anchorId,
                MerkleRoot = batch.MerkleRoot,
                AnchorTimestamp = VCPUtility.GetISOTimestamp(now),
                AnchorTarget = "LOCAL_FILE",
                AnchorProof = ComputeAnchorProof(batch, now),
                EventCount = batch.EventCount,
                BatchStartTime = batch.StartTime,
                BatchEndTime = batch.EndTime,
                ConformanceTier = batch.ConformanceTier
            };

            // アンカーレコードファイル保存
            var anchorPath = Path.Combine(_config.LocalStoragePath, $"{anchorId}.json");
            var anchorJson = JsonSerializer.Serialize(anchorRecord, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(anchorPath, anchorJson, Encoding.UTF8);

            // バッチ詳細ファイル保存
            var batchPath = Path.Combine(_config.LocalStoragePath, $"{anchorId}_batch.json");
            var batchJson = JsonSerializer.Serialize(batch, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(batchPath, batchJson, Encoding.UTF8);

            Log($"[VCPAnchor] ローカル保存完了: {anchorPath}");

            return new AnchorResult
            {
                Success = true,
                AnchorRecord = anchorRecord,
                TargetUsed = AnchorTargetType.LOCAL_FILE
            };
        }

        /// <summary>
        /// OpenTimestampsへのアンカリング
        /// </summary>
        private async Task<AnchorResult> AnchorToOpenTimestampsAsync(MerkleBatch batch)
        {
            // OpenTimestamps APIエンドポイント
            const string OTS_API = "https://alice.btc.calendar.opentimestamps.org/digest";

            var digestBytes = HexToBytes(batch.MerkleRoot);
            var content = new ByteArrayContent(digestBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

            var response = await _httpClient.PostAsync(OTS_API, content);
            
            if (!response.IsSuccessStatusCode)
            {
                return new AnchorResult
                {
                    Success = false,
                    ErrorMessage = $"OpenTimestamps API エラー: {response.StatusCode}",
                    TargetUsed = AnchorTargetType.OPENTIMESTAMPS
                };
            }

            var otsProof = await response.Content.ReadAsByteArrayAsync();
            var otsProofHex = BitConverter.ToString(otsProof).Replace("-", "").ToLower();

            var now = DateTimeOffset.UtcNow;
            var anchorId = $"ots_{now:yyyyMMdd_HHmmss}_{batch.BatchID.Substring(0, 8)}";

            // ローカルにも保存
            Directory.CreateDirectory(_config.LocalStoragePath);
            var otsPath = Path.Combine(_config.LocalStoragePath, $"{anchorId}.ots");
            await File.WriteAllBytesAsync(otsPath, otsProof);

            var anchorRecord = new AnchorRecord
            {
                AnchorID = anchorId,
                MerkleRoot = batch.MerkleRoot,
                AnchorTimestamp = VCPUtility.GetISOTimestamp(now),
                AnchorTarget = "OPENTIMESTAMPS",
                AnchorProof = otsProofHex.Substring(0, Math.Min(128, otsProofHex.Length)) + "...",
                EventCount = batch.EventCount,
                BatchStartTime = batch.StartTime,
                BatchEndTime = batch.EndTime,
                ConformanceTier = batch.ConformanceTier
            };

            Log($"[VCPAnchor] OpenTimestamps成功: {otsPath}");

            return new AnchorResult
            {
                Success = true,
                AnchorRecord = anchorRecord,
                TargetUsed = AnchorTargetType.OPENTIMESTAMPS
            };
        }

        /// <summary>
        /// FreeTSAへのアンカリング（RFC 3161）
        /// </summary>
        private async Task<AnchorResult> AnchorToFreeTSAAsync(MerkleBatch batch)
        {
            // FreeTSA.org エンドポイント
            const string TSA_URL = "https://freetsa.org/tsr";

            // TSAリクエスト作成（簡易版）
            // 注: 完全なRFC 3161実装にはBouncyCastle等が必要
            var tsRequest = CreateSimpleTSRequest(batch.MerkleRoot);

            var content = new ByteArrayContent(tsRequest);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/timestamp-query");

            var response = await _httpClient.PostAsync(TSA_URL, content);

            if (!response.IsSuccessStatusCode)
            {
                return new AnchorResult
                {
                    Success = false,
                    ErrorMessage = $"FreeTSA API エラー: {response.StatusCode}",
                    TargetUsed = AnchorTargetType.FREE_TSA
                };
            }

            var tsResponse = await response.Content.ReadAsByteArrayAsync();
            var tsResponseHex = BitConverter.ToString(tsResponse).Replace("-", "").ToLower();

            var now = DateTimeOffset.UtcNow;
            var anchorId = $"tsa_{now:yyyyMMdd_HHmmss}_{batch.BatchID.Substring(0, 8)}";

            // ローカルにも保存
            Directory.CreateDirectory(_config.LocalStoragePath);
            var tsrPath = Path.Combine(_config.LocalStoragePath, $"{anchorId}.tsr");
            await File.WriteAllBytesAsync(tsrPath, tsResponse);

            var anchorRecord = new AnchorRecord
            {
                AnchorID = anchorId,
                MerkleRoot = batch.MerkleRoot,
                AnchorTimestamp = VCPUtility.GetISOTimestamp(now),
                AnchorTarget = "FREE_TSA",
                AnchorProof = tsResponseHex.Substring(0, Math.Min(128, tsResponseHex.Length)) + "...",
                EventCount = batch.EventCount,
                BatchStartTime = batch.StartTime,
                BatchEndTime = batch.EndTime,
                ConformanceTier = batch.ConformanceTier
            };

            Log($"[VCPAnchor] FreeTSA成功: {tsrPath}");

            return new AnchorResult
            {
                Success = true,
                AnchorRecord = anchorRecord,
                TargetUsed = AnchorTargetType.FREE_TSA
            };
        }

        /// <summary>
        /// カスタムHTTPエンドポイントへのアンカリング
        /// </summary>
        private async Task<AnchorResult> AnchorToCustomHttpAsync(MerkleBatch batch)
        {
            if (string.IsNullOrEmpty(_config.CustomHttpEndpoint))
            {
                return new AnchorResult
                {
                    Success = false,
                    ErrorMessage = "カスタムHTTPエンドポイントが設定されていません",
                    TargetUsed = AnchorTargetType.CUSTOM_HTTP
                };
            }

            var payload = new
            {
                batch_id = batch.BatchID,
                merkle_root = batch.MerkleRoot,
                event_count = batch.EventCount,
                start_time = batch.StartTime,
                end_time = batch.EndTime,
                tier = batch.ConformanceTier.ToString()
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // カスタムヘッダー追加
            foreach (var header in _config.CustomHttpHeaders)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            var response = await _httpClient.PostAsync(_config.CustomHttpEndpoint, content);

            if (!response.IsSuccessStatusCode)
            {
                return new AnchorResult
                {
                    Success = false,
                    ErrorMessage = $"カスタムHTTP エラー: {response.StatusCode}",
                    TargetUsed = AnchorTargetType.CUSTOM_HTTP
                };
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            var now = DateTimeOffset.UtcNow;
            var anchorId = $"http_{now:yyyyMMdd_HHmmss}_{batch.BatchID.Substring(0, 8)}";

            var anchorRecord = new AnchorRecord
            {
                AnchorID = anchorId,
                MerkleRoot = batch.MerkleRoot,
                AnchorTimestamp = VCPUtility.GetISOTimestamp(now),
                AnchorTarget = _config.CustomHttpEndpoint,
                AnchorProof = responseBody.Length > 256 ? responseBody.Substring(0, 256) + "..." : responseBody,
                EventCount = batch.EventCount,
                BatchStartTime = batch.StartTime,
                BatchEndTime = batch.EndTime,
                ConformanceTier = batch.ConformanceTier
            };

            Log($"[VCPAnchor] カスタムHTTP成功: {_config.CustomHttpEndpoint}");

            return new AnchorResult
            {
                Success = true,
                AnchorRecord = anchorRecord,
                TargetUsed = AnchorTargetType.CUSTOM_HTTP
            };
        }

        #endregion

        #region ユーティリティ

        /// <summary>
        /// アンカー証明を計算
        /// </summary>
        private string ComputeAnchorProof(MerkleBatch batch, DateTimeOffset timestamp)
        {
            var proofInput = $"{batch.MerkleRoot}|{timestamp:O}|{batch.EventCount}";
            return VCPUtility.ComputeSHA256(proofInput);
        }

        /// <summary>
        /// 簡易TSリクエスト作成
        /// 注: 完全なRFC 3161実装ではない
        /// </summary>
        private byte[] CreateSimpleTSRequest(string merkleRoot)
        {
            var hashBytes = HexToBytes(merkleRoot);
            
            // 簡易ASN.1エンコード（テスト用）
            // 本番環境ではBouncyCastle等を使用すること
            var request = new List<byte>();
            
            // TimeStampReq ::= SEQUENCE
            request.Add(0x30); // SEQUENCE
            request.Add(0x00); // 長さ（後で設定）
            
            // version INTEGER
            request.Add(0x02); // INTEGER
            request.Add(0x01); // 長さ
            request.Add(0x01); // version = 1
            
            // messageImprint MessageImprint
            request.Add(0x30); // SEQUENCE
            request.Add((byte)(2 + 2 + 11 + 2 + hashBytes.Length)); // 長さ
            
            // hashAlgorithm AlgorithmIdentifier (SHA-256)
            request.Add(0x30); // SEQUENCE
            request.Add(0x0D); // 長さ
            request.Add(0x06); // OID
            request.Add(0x09); // 長さ
            // SHA-256 OID: 2.16.840.1.101.3.4.2.1
            request.AddRange(new byte[] { 0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x01 });
            request.Add(0x05); // NULL
            request.Add(0x00);
            
            // hashedMessage OCTET STRING
            request.Add(0x04); // OCTET STRING
            request.Add((byte)hashBytes.Length);
            request.AddRange(hashBytes);
            
            // 長さを設定
            var result = request.ToArray();
            result[1] = (byte)(result.Length - 2);
            
            return result;
        }

        /// <summary>
        /// 16進文字列をバイト配列に変換
        /// </summary>
        private static byte[] HexToBytes(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
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
        /// アンカー履歴をファイルに保存
        /// </summary>
        public async Task SaveAnchorHistoryAsync()
        {
            Directory.CreateDirectory(_config.LocalStoragePath);
            var historyPath = Path.Combine(_config.LocalStoragePath, "anchor_history.json");
            
            var json = JsonSerializer.Serialize(_anchorHistory, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(historyPath, json, Encoding.UTF8);
            
            Log($"[VCPAnchor] 履歴保存: {historyPath}");
        }

        /// <summary>
        /// アンカー履歴をファイルから読込
        /// </summary>
        public async Task LoadAnchorHistoryAsync()
        {
            var historyPath = Path.Combine(_config.LocalStoragePath, "anchor_history.json");
            
            if (File.Exists(historyPath))
            {
                var json = await File.ReadAllTextAsync(historyPath, Encoding.UTF8);
                var history = JsonSerializer.Deserialize<List<AnchorRecord>>(json);
                
                lock (_lockObject)
                {
                    _anchorHistory.Clear();
                    _anchorHistory.AddRange(history);
                    
                    if (_anchorHistory.Count > 0)
                    {
                        var lastAnchor = _anchorHistory[_anchorHistory.Count - 1];
                        _lastAnchorTime = DateTimeOffset.Parse(lastAnchor.AnchorTimestamp);
                    }
                }
                
                Log($"[VCPAnchor] 履歴読込: {_anchorHistory.Count}件");
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        #endregion
    }
}
