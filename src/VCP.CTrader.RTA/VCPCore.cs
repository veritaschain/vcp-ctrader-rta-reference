// ================================================================================
// VCPCore.cs - VeritasChain Protocol (VCP) v1.1 Core Data Structures for cTrader
// ================================================================================
// VCP仕様書 v1.1準拠 - Silver Tier向けリテール取引システム用
// cTrader cBot プラグインとして動作
// 
// Copyright (c) 2025 VeritasChain Standards Organization (VSO)
// License: CC BY 4.0 International
// ================================================================================

using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Collections.Generic;

namespace VCP.CTrader
{
    #region Enumerations (VCP v1.1 Section 1.5)

    /// <summary>
    /// VCP署名アルゴリズム列挙型 (Section 1.5.1)
    /// </summary>
    public enum SignAlgo
    {
        ED25519,           // Edwards-curve Digital Signature (DEFAULT)
        ECDSA_SECP256K1,   // Bitcoin/Ethereum compatible
        RSA_2048,          // Legacy systems (DEPRECATED)
        DILITHIUM2,        // Post-quantum (FUTURE)
        FALCON512          // Post-quantum (FUTURE)
    }

    /// <summary>
    /// VCPハッシュアルゴリズム列挙型 (Section 1.5.2)
    /// </summary>
    public enum HashAlgo
    {
        SHA256,     // SHA-2 family, 256-bit (DEFAULT)
        SHA3_256,   // SHA-3 family, 256-bit
        BLAKE3,     // High-performance hash
        SHA3_512    // SHA-3 family, 512-bit (FUTURE)
    }

    /// <summary>
    /// VCPクロック同期ステータス列挙型 (Section 1.5.3)
    /// </summary>
    public enum ClockSyncStatus
    {
        PTP_LOCKED,    // PTP synchronized with lock (Platinum)
        NTP_SYNCED,    // NTP synchronized (Gold)
        BEST_EFFORT,   // Best-effort synchronization (Silver)
        UNRELIABLE     // No reliable synchronization (Silver degraded)
    }

    /// <summary>
    /// VCPタイムスタンプ精度列挙型 (Section 1.5.4)
    /// </summary>
    public enum TimestampPrecision
    {
        NANOSECOND,    // 9 decimal places
        MICROSECOND,   // 6 decimal places
        MILLISECOND    // 3 decimal places (Silver tier)
    }

    /// <summary>
    /// VCPコンプライアンス階層 (Section 2.1)
    /// </summary>
    public enum ConformanceTier
    {
        PLATINUM,  // HFT/Exchange - PTPv2, SBE, 10min anchor
        GOLD,      // Prop/Institutional - NTP, JSON, 1hr anchor
        SILVER     // Retail/cTrader - Best-effort, JSON, 24hr anchor
    }

    /// <summary>
    /// VCPイベントタイプ (Section 3.2)
    /// </summary>
    public enum VCPEventType
    {
        // 取引イベント
        INIT,       // システム開始
        SIG,        // シグナル生成
        ORD,        // 注文送信
        ACK,        // 注文受理
        EXE,        // 約定（全量）
        PRT,        // 部分約定
        REJ,        // 拒否
        CXL,        // キャンセル
        MOD,        // 変更
        CLS,        // ポジション決済

        // エラーイベント (Section 3.2.1 - NEW in v1.1)
        ERR_CONN,      // 接続エラー
        ERR_AUTH,      // 認証エラー
        ERR_TIMEOUT,   // タイムアウト
        ERR_REJECT,    // 拒否
        ERR_PARSE,     // パースエラー
        ERR_SYNC,      // 同期エラー
        ERR_RISK,      // リスクエラー
        ERR_SYSTEM,    // システムエラー
        ERR_RECOVER,   // リカバリー

        // VCP管理イベント
        VCP_ANCHOR,    // アンカリングイベント
        VCP_BATCH,     // バッチ完了
        VCP_VERIFY     // 検証イベント
    }

    /// <summary>
    /// エラー重大度 (Section 3.2.1)
    /// </summary>
    public enum ErrorSeverity
    {
        INFO,
        WARNING,
        CRITICAL
    }

    /// <summary>
    /// VCP-XREF パーティロール (Section 5.6.4)
    /// </summary>
    public enum PartyRole
    {
        INITIATOR,      // トランザクション開始者
        COUNTERPARTY,   // カウンターパーティ
        OBSERVER        // オブザーバー（読み取り専用）
    }

    /// <summary>
    /// 照合ステータス (Section 5.6.3)
    /// </summary>
    public enum ReconciliationStatus
    {
        PENDING,      // 照合待ち
        MATCHED,      // 一致
        DISCREPANCY,  // 不一致
        TIMEOUT       // タイムアウト
    }

    #endregion

    #region Core Data Models (VCP v1.1 Section 4)

    /// <summary>
    /// VCPイベントヘッダー (Section 4 - VCP-CORE)
    /// </summary>
    public class VCPHeader
    {
        /// <summary>
        /// VCPプロトコルバージョン
        /// </summary>
        [JsonPropertyName("Version")]
        public string Version { get; set; } = "1.1";

        /// <summary>
        /// イベントID (UUID v7 - RFC 9562)
        /// </summary>
        [JsonPropertyName("EventID")]
        public string EventID { get; set; }

        /// <summary>
        /// イベントタイプ
        /// </summary>
        [JsonPropertyName("EventType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public VCPEventType EventType { get; set; }

        /// <summary>
        /// ISO 8601タイムスタンプ (UTC)
        /// </summary>
        [JsonPropertyName("TimestampISO")]
        public string TimestampISO { get; set; }

        /// <summary>
        /// UNIXタイムスタンプ（マイクロ秒）
        /// </summary>
        [JsonPropertyName("TimestampInt")]
        public long TimestampInt { get; set; }

        /// <summary>
        /// ハッシュアルゴリズム
        /// </summary>
        [JsonPropertyName("HashAlgo")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public HashAlgo HashAlgo { get; set; } = HashAlgo.SHA256;

        /// <summary>
        /// 署名アルゴリズム
        /// </summary>
        [JsonPropertyName("SignAlgo")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public SignAlgo SignAlgo { get; set; } = SignAlgo.ED25519;

        /// <summary>
        /// 前イベントハッシュ（OPTIONAL in v1.1）
        /// </summary>
        [JsonPropertyName("PrevHash")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string PrevHash { get; set; }

        /// <summary>
        /// イベントハッシュ
        /// </summary>
        [JsonPropertyName("EventHash")]
        public string EventHash { get; set; }

        /// <summary>
        /// クロック同期ステータス
        /// </summary>
        [JsonPropertyName("ClockSyncStatus")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ClockSyncStatus ClockSyncStatus { get; set; } = ClockSyncStatus.BEST_EFFORT;

        /// <summary>
        /// タイムスタンプ精度
        /// </summary>
        [JsonPropertyName("TimestampPrecision")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TimestampPrecision TimestampPrecision { get; set; } = TimestampPrecision.MILLISECOND;
    }

    /// <summary>
    /// ポリシー識別 (Section 5.5 - NEW in v1.1)
    /// </summary>
    public class PolicyIdentification
    {
        /// <summary>
        /// VCPバージョン
        /// </summary>
        [JsonPropertyName("Version")]
        public string Version { get; set; } = "1.1";

        /// <summary>
        /// ポリシーID (形式: reverse_domain:local_id)
        /// </summary>
        [JsonPropertyName("PolicyID")]
        public string PolicyID { get; set; }

        /// <summary>
        /// コンプライアンス階層
        /// </summary>
        [JsonPropertyName("ConformanceTier")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ConformanceTier ConformanceTier { get; set; }

        /// <summary>
        /// 登録ポリシー詳細
        /// </summary>
        [JsonPropertyName("RegistrationPolicy")]
        public RegistrationPolicy RegistrationPolicy { get; set; }

        /// <summary>
        /// 検証深度
        /// </summary>
        [JsonPropertyName("VerificationDepth")]
        public VerificationDepth VerificationDepth { get; set; }
    }

    /// <summary>
    /// 登録ポリシー詳細
    /// </summary>
    public class RegistrationPolicy
    {
        /// <summary>
        /// 発行者（組織名）
        /// </summary>
        [JsonPropertyName("Issuer")]
        public string Issuer { get; set; }

        /// <summary>
        /// ポリシードキュメントURI
        /// </summary>
        [JsonPropertyName("PolicyURI")]
        public string PolicyURI { get; set; }

        /// <summary>
        /// 発効日時（UNIXタイムスタンプ）
        /// </summary>
        [JsonPropertyName("EffectiveDate")]
        public long EffectiveDate { get; set; }

        /// <summary>
        /// 失効日時（UNIXタイムスタンプ、オプション）
        /// </summary>
        [JsonPropertyName("ExpirationDate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public long ExpirationDate { get; set; }
    }

    /// <summary>
    /// 検証深度設定
    /// </summary>
    public class VerificationDepth
    {
        /// <summary>
        /// ハッシュチェーン検証の有無
        /// </summary>
        [JsonPropertyName("HashChainValidation")]
        public bool HashChainValidation { get; set; } = false;

        /// <summary>
        /// Merkle証明必須（v1.1では常にtrue）
        /// </summary>
        [JsonPropertyName("MerkleProofRequired")]
        public bool MerkleProofRequired { get; set; } = true;

        /// <summary>
        /// 外部アンカー必須（v1.1では常にtrue）
        /// </summary>
        [JsonPropertyName("ExternalAnchorRequired")]
        public bool ExternalAnchorRequired { get; set; } = true;
    }

    /// <summary>
    /// エラー詳細 (Section 3.2.1)
    /// </summary>
    public class ErrorDetails
    {
        /// <summary>
        /// エラーコード
        /// </summary>
        [JsonPropertyName("ErrorCode")]
        public string ErrorCode { get; set; }

        /// <summary>
        /// エラーメッセージ
        /// </summary>
        [JsonPropertyName("ErrorMessage")]
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 重大度
        /// </summary>
        [JsonPropertyName("Severity")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ErrorSeverity Severity { get; set; }

        /// <summary>
        /// 影響を受けたコンポーネント
        /// </summary>
        [JsonPropertyName("AffectedComponent")]
        public string AffectedComponent { get; set; }

        /// <summary>
        /// リカバリーアクション
        /// </summary>
        [JsonPropertyName("RecoveryAction")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string RecoveryAction { get; set; }

        /// <summary>
        /// 関連イベントID
        /// </summary>
        [JsonPropertyName("CorrelatedEventID")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string CorrelatedEventID { get; set; }
    }

    /// <summary>
    /// トレードペイロード (VCP-TRADE)
    /// </summary>
    public class TradePayload
    {
        /// <summary>
        /// 取引シンボル
        /// </summary>
        [JsonPropertyName("Symbol")]
        public string Symbol { get; set; }

        /// <summary>
        /// 注文ID
        /// </summary>
        [JsonPropertyName("OrderID")]
        public string OrderID { get; set; }

        /// <summary>
        /// ポジションID
        /// </summary>
        [JsonPropertyName("PositionID")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string PositionID { get; set; }

        /// <summary>
        /// 売買方向 (BUY/SELL)
        /// </summary>
        [JsonPropertyName("Side")]
        public string Side { get; set; }

        /// <summary>
        /// 数量
        /// </summary>
        [JsonPropertyName("Volume")]
        public double Volume { get; set; }

        /// <summary>
        /// 価格
        /// </summary>
        [JsonPropertyName("Price")]
        public double Price { get; set; }

        /// <summary>
        /// ストップロス
        /// </summary>
        [JsonPropertyName("StopLoss")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double StopLoss { get; set; }

        /// <summary>
        /// テイクプロフィット
        /// </summary>
        [JsonPropertyName("TakeProfit")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double TakeProfit { get; set; }

        /// <summary>
        /// コメント
        /// </summary>
        [JsonPropertyName("Comment")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Comment { get; set; }

        /// <summary>
        /// 損益
        /// </summary>
        [JsonPropertyName("Profit")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double Profit { get; set; }

        /// <summary>
        /// スワップ
        /// </summary>
        [JsonPropertyName("Swap")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double Swap { get; set; }

        /// <summary>
        /// 手数料
        /// </summary>
        [JsonPropertyName("Commission")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double Commission { get; set; }

        /// <summary>
        /// スプレッド（pips）
        /// </summary>
        [JsonPropertyName("SpreadPips")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double SpreadPips { get; set; }

        /// <summary>
        /// ブローカー名
        /// </summary>
        [JsonPropertyName("Broker")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Broker { get; set; }

        /// <summary>
        /// アカウントID
        /// </summary>
        [JsonPropertyName("AccountID")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string AccountID { get; set; }
    }

    /// <summary>
    /// AI/アルゴリズムガバナンス情報 (VCP-GOV)
    /// </summary>
    public class GovernancePayload
    {
        /// <summary>
        /// アルゴリズム名
        /// </summary>
        [JsonPropertyName("AlgorithmName")]
        public string AlgorithmName { get; set; }

        /// <summary>
        /// アルゴリズムバージョン
        /// </summary>
        [JsonPropertyName("AlgorithmVersion")]
        public string AlgorithmVersion { get; set; }

        /// <summary>
        /// 判断理由
        /// </summary>
        [JsonPropertyName("DecisionReason")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string DecisionReason { get; set; }

        /// <summary>
        /// 信頼度スコア (0-100)
        /// </summary>
        [JsonPropertyName("ConfidenceScore")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double ConfidenceScore { get; set; }

        /// <summary>
        /// 使用モデル一覧
        /// </summary>
        [JsonPropertyName("ModelsUsed")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string> ModelsUsed { get; set; }

        /// <summary>
        /// 入力フィーチャー
        /// </summary>
        [JsonPropertyName("InputFeatures")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, object> InputFeatures { get; set; }
    }

    /// <summary>
    /// リスク管理情報 (VCP-RISK)
    /// </summary>
    public class RiskPayload
    {
        /// <summary>
        /// ポジションサイズ（ロット）
        /// </summary>
        [JsonPropertyName("PositionSize")]
        public double PositionSize { get; set; }

        /// <summary>
        /// リスク率（%）
        /// </summary>
        [JsonPropertyName("RiskPercentage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double RiskPercentage { get; set; }

        /// <summary>
        /// 最大ドローダウン
        /// </summary>
        [JsonPropertyName("MaxDrawdown")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double MaxDrawdown { get; set; }

        /// <summary>
        /// 現在ドローダウン
        /// </summary>
        [JsonPropertyName("CurrentDrawdown")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double CurrentDrawdown { get; set; }

        /// <summary>
        /// 日次損失限度
        /// </summary>
        [JsonPropertyName("DailyLossLimit")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double DailyLossLimit { get; set; }

        /// <summary>
        /// 日次損益
        /// </summary>
        [JsonPropertyName("DailyPnL")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double DailyPnL { get; set; }

        /// <summary>
        /// エクスポージャー
        /// </summary>
        [JsonPropertyName("Exposure")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double Exposure { get; set; }
    }

    /// <summary>
    /// VCPイベント完全形式
    /// </summary>
    public class VCPEvent
    {
        /// <summary>
        /// イベントヘッダー
        /// </summary>
        [JsonPropertyName("Header")]
        public VCPHeader Header { get; set; }

        /// <summary>
        /// ポリシー識別
        /// </summary>
        [JsonPropertyName("PolicyIdentification")]
        public PolicyIdentification PolicyIdentification { get; set; }

        /// <summary>
        /// トレードペイロード
        /// </summary>
        [JsonPropertyName("Trade")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TradePayload Trade { get; set; }

        /// <summary>
        /// ガバナンスペイロード
        /// </summary>
        [JsonPropertyName("Governance")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GovernancePayload Governance { get; set; }

        /// <summary>
        /// リスクペイロード
        /// </summary>
        [JsonPropertyName("Risk")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public RiskPayload Risk { get; set; }

        /// <summary>
        /// エラー詳細
        /// </summary>
        [JsonPropertyName("ErrorDetails")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ErrorDetails ErrorDetails { get; set; }

        /// <summary>
        /// 拡張フィールド
        /// </summary>
        [JsonPropertyName("Extensions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, object> Extensions { get; set; }
    }

    /// <summary>
    /// アンカーレコード (Section 6.3.3)
    /// </summary>
    public class AnchorRecord
    {
        /// <summary>
        /// アンカーID
        /// </summary>
        [JsonPropertyName("AnchorID")]
        public string AnchorID { get; set; }

        /// <summary>
        /// Merkleルート
        /// </summary>
        [JsonPropertyName("MerkleRoot")]
        public string MerkleRoot { get; set; }

        /// <summary>
        /// アンカータイムスタンプ
        /// </summary>
        [JsonPropertyName("AnchorTimestamp")]
        public string AnchorTimestamp { get; set; }

        /// <summary>
        /// アンカーターゲット
        /// </summary>
        [JsonPropertyName("AnchorTarget")]
        public string AnchorTarget { get; set; }

        /// <summary>
        /// アンカー証明（トランザクションハッシュ等）
        /// </summary>
        [JsonPropertyName("AnchorProof")]
        public string AnchorProof { get; set; }

        /// <summary>
        /// バッチ内イベント数
        /// </summary>
        [JsonPropertyName("EventCount")]
        public int EventCount { get; set; }

        /// <summary>
        /// バッチ期間開始
        /// </summary>
        [JsonPropertyName("BatchStartTime")]
        public string BatchStartTime { get; set; }

        /// <summary>
        /// バッチ期間終了
        /// </summary>
        [JsonPropertyName("BatchEndTime")]
        public string BatchEndTime { get; set; }

        /// <summary>
        /// コンプライアンス階層
        /// </summary>
        [JsonPropertyName("ConformanceTier")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ConformanceTier ConformanceTier { get; set; }

        /// <summary>
        /// 署名
        /// </summary>
        [JsonPropertyName("Signature")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Signature { get; set; }
    }

    #endregion

    #region Utility Classes

    /// <summary>
    /// VCPユーティリティクラス
    /// </summary>
    public static class VCPUtility
    {
        private static readonly object _lockObject = new object();
        private static long _lastTimestamp = 0;
        private static int _sequence = 0;

        /// <summary>
        /// UUID v7互換のイベントIDを生成
        /// RFC 9562準拠
        /// </summary>
        public static string GenerateEventID()
        {
            // UUID v7形式: タイムスタンプベース + ランダム
            var now = DateTimeOffset.UtcNow;
            var timestamp = now.ToUnixTimeMilliseconds();
            
            lock (_lockObject)
            {
                if (timestamp == _lastTimestamp)
                {
                    _sequence++;
                }
                else
                {
                    _lastTimestamp = timestamp;
                    _sequence = 0;
                }
            }
            
            var random = new byte[10];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(random);
            }

            // UUID v7フォーマット
            var uuid = new byte[16];
            
            // タイムスタンプ（48ビット）
            uuid[0] = (byte)((timestamp >> 40) & 0xFF);
            uuid[1] = (byte)((timestamp >> 32) & 0xFF);
            uuid[2] = (byte)((timestamp >> 24) & 0xFF);
            uuid[3] = (byte)((timestamp >> 16) & 0xFF);
            uuid[4] = (byte)((timestamp >> 8) & 0xFF);
            uuid[5] = (byte)(timestamp & 0xFF);
            
            // バージョン7
            uuid[6] = (byte)(0x70 | (random[0] & 0x0F));
            uuid[7] = random[1];
            
            // バリアント
            uuid[8] = (byte)(0x80 | (random[2] & 0x3F));
            
            // ランダム
            Array.Copy(random, 3, uuid, 9, 7);

            return FormatUuid(uuid);
        }

        private static string FormatUuid(byte[] uuid)
        {
            return $"{BitConverter.ToString(uuid, 0, 4).Replace("-", "").ToLower()}-" +
                   $"{BitConverter.ToString(uuid, 4, 2).Replace("-", "").ToLower()}-" +
                   $"{BitConverter.ToString(uuid, 6, 2).Replace("-", "").ToLower()}-" +
                   $"{BitConverter.ToString(uuid, 8, 2).Replace("-", "").ToLower()}-" +
                   $"{BitConverter.ToString(uuid, 10, 6).Replace("-", "").ToLower()}";
        }

        /// <summary>
        /// ISOタイムスタンプを生成
        /// </summary>
        public static string GetISOTimestamp(DateTimeOffset? dt = null)
        {
            var timestamp = dt ?? DateTimeOffset.UtcNow;
            return timestamp.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");
        }

        /// <summary>
        /// UNIXマイクロ秒タイムスタンプを取得
        /// </summary>
        public static long GetUnixMicroseconds(DateTimeOffset? dt = null)
        {
            var timestamp = dt ?? DateTimeOffset.UtcNow;
            return timestamp.ToUnixTimeMilliseconds() * 1000;
        }

        /// <summary>
        /// SHA-256ハッシュを計算
        /// </summary>
        public static string ComputeSHA256(string input)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(input);
                var hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        /// <summary>
        /// JSONを正規化（RFC 8785 JCS簡易版）
        /// </summary>
        public static string CanonicalizeJson(object obj)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = null // PascalCase維持
            };
            
            // JSONシリアライズ
            var json = JsonSerializer.Serialize(obj, options);
            
            // 再パースしてソート
            using (var doc = JsonDocument.Parse(json))
            {
                return SerializeCanonical(doc.RootElement);
            }
        }

        private static string SerializeCanonical(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var properties = new SortedDictionary<string, string>();
                    foreach (var prop in element.EnumerateObject())
                    {
                        properties[prop.Name] = SerializeCanonical(prop.Value);
                    }
                    var objectParts = new List<string>();
                    foreach (var kvp in properties)
                    {
                        objectParts.Add($"\"{kvp.Key}\":{kvp.Value}");
                    }
                    return "{" + string.Join(",", objectParts) + "}";

                case JsonValueKind.Array:
                    var arrayParts = new List<string>();
                    foreach (var item in element.EnumerateArray())
                    {
                        arrayParts.Add(SerializeCanonical(item));
                    }
                    return "[" + string.Join(",", arrayParts) + "]";

                case JsonValueKind.String:
                    return JsonSerializer.Serialize(element.GetString());

                case JsonValueKind.Number:
                    if (element.TryGetInt64(out long intVal))
                        return intVal.ToString();
                    return element.GetDouble().ToString("G17");

                case JsonValueKind.True:
                    return "true";

                case JsonValueKind.False:
                    return "false";

                case JsonValueKind.Null:
                    return "null";

                default:
                    return element.GetRawText();
            }
        }

        /// <summary>
        /// イベントハッシュを計算 (Section 6.1.1)
        /// </summary>
        public static string ComputeEventHash(VCPHeader header, object payload)
        {
            // ヘッダーからEventHashを除外してハッシュ計算
            var headerCopy = new
            {
                header.Version,
                header.EventID,
                header.EventType,
                header.TimestampISO,
                header.TimestampInt,
                header.HashAlgo,
                header.SignAlgo,
                header.PrevHash,
                header.ClockSyncStatus,
                header.TimestampPrecision
            };

            var canonicalHeader = CanonicalizeJson(headerCopy);
            var canonicalPayload = CanonicalizeJson(payload);
            var hashInput = canonicalHeader + canonicalPayload;

            return ComputeSHA256(hashInput);
        }
    }

    #endregion
}
