// ================================================================================
// VCPMerkleTree.cs - VCP v1.1 Merkle Tree Implementation (RFC 6962 Compliant)
// ================================================================================
// VCP仕様書 v1.1 Section 6.2準拠 - Merkleツリー構築
// cTrader cBot プラグインとして動作
// ================================================================================

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;

namespace VCP.CTrader
{
    /// <summary>
    /// Merkleツリーノード
    /// </summary>
    public class MerkleNode
    {
        /// <summary>
        /// ノードハッシュ
        /// </summary>
        public byte[] Hash { get; set; }

        /// <summary>
        /// 左子ノード
        /// </summary>
        public MerkleNode Left { get; set; }

        /// <summary>
        /// 右子ノード
        /// </summary>
        public MerkleNode Right { get; set; }

        /// <summary>
        /// リーフノードかどうか
        /// </summary>
        public bool IsLeaf { get; set; }

        /// <summary>
        /// リーフインデックス（リーフノードのみ）
        /// </summary>
        public int LeafIndex { get; set; } = -1;

        /// <summary>
        /// ハッシュを16進文字列で取得
        /// </summary>
        public string HashHex => BitConverter.ToString(Hash).Replace("-", "").ToLower();
    }

    /// <summary>
    /// Merkle監査パス項目
    /// </summary>
    public class AuditPathItem
    {
        /// <summary>
        /// ハッシュ値
        /// </summary>
        [JsonPropertyName("hash")]
        public string Hash { get; set; }

        /// <summary>
        /// 位置（left/right）
        /// </summary>
        [JsonPropertyName("position")]
        public string Position { get; set; }
    }

    /// <summary>
    /// Merkle包含証明
    /// </summary>
    public class MerkleInclusionProof
    {
        /// <summary>
        /// イベントID
        /// </summary>
        [JsonPropertyName("EventID")]
        public string EventID { get; set; }

        /// <summary>
        /// イベントハッシュ
        /// </summary>
        [JsonPropertyName("EventHash")]
        public string EventHash { get; set; }

        /// <summary>
        /// リーフインデックス
        /// </summary>
        [JsonPropertyName("LeafIndex")]
        public int LeafIndex { get; set; }

        /// <summary>
        /// Merkleルート
        /// </summary>
        [JsonPropertyName("MerkleRoot")]
        public string MerkleRoot { get; set; }

        /// <summary>
        /// 監査パス
        /// </summary>
        [JsonPropertyName("AuditPath")]
        public List<AuditPathItem> AuditPath { get; set; }

        /// <summary>
        /// ツリーサイズ（リーフ数）
        /// </summary>
        [JsonPropertyName("TreeSize")]
        public int TreeSize { get; set; }
    }

    /// <summary>
    /// VCP v1.1 Merkleツリー実装
    /// RFC 6962準拠 - 第二プレイメージ攻撃対策済み
    /// </summary>
    public class VCPMerkleTree
    {
        // RFC 6962ドメイン分離プレフィックス
        private const byte LEAF_PREFIX = 0x00;
        private const byte INTERNAL_PREFIX = 0x01;

        /// <summary>
        /// ルートノード
        /// </summary>
        public MerkleNode Root { get; private set; }

        /// <summary>
        /// リーフノード一覧
        /// </summary>
        public List<MerkleNode> Leaves { get; private set; }

        /// <summary>
        /// 全レベル（デバッグ用）
        /// </summary>
        public List<List<MerkleNode>> Levels { get; private set; }

        /// <summary>
        /// イベントハッシュ一覧
        /// </summary>
        private List<string> _eventHashes;

        /// <summary>
        /// イベントID→インデックスマッピング
        /// </summary>
        private Dictionary<string, int> _eventIdToIndex;

        /// <summary>
        /// 空のツリーを作成
        /// </summary>
        public VCPMerkleTree()
        {
            Leaves = new List<MerkleNode>();
            Levels = new List<List<MerkleNode>>();
            _eventHashes = new List<string>();
            _eventIdToIndex = new Dictionary<string, int>();
        }

        /// <summary>
        /// イベントハッシュ一覧からMerkleツリーを構築
        /// </summary>
        /// <param name="events">VCPイベント一覧</param>
        public void BuildFromEvents(List<VCPEvent> events)
        {
            if (events == null || events.Count == 0)
            {
                throw new ArgumentException("イベントリストが空です");
            }

            _eventHashes = new List<string>();
            _eventIdToIndex = new Dictionary<string, int>();

            for (int i = 0; i < events.Count; i++)
            {
                var evt = events[i];
                _eventHashes.Add(evt.Header.EventHash);
                _eventIdToIndex[evt.Header.EventID] = i;
            }

            BuildTree(_eventHashes);
        }

        /// <summary>
        /// ハッシュ一覧からMerkleツリーを構築
        /// </summary>
        /// <param name="eventHashes">イベントハッシュ一覧（16進文字列）</param>
        public void BuildFromHashes(List<string> eventHashes)
        {
            if (eventHashes == null || eventHashes.Count == 0)
            {
                throw new ArgumentException("ハッシュリストが空です");
            }

            _eventHashes = new List<string>(eventHashes);
            _eventIdToIndex = new Dictionary<string, int>();
            
            for (int i = 0; i < eventHashes.Count; i++)
            {
                _eventIdToIndex[eventHashes[i]] = i; // ハッシュをキーとして使用
            }

            BuildTree(eventHashes);
        }

        /// <summary>
        /// Merkleツリーを構築（内部メソッド）
        /// RFC 6962準拠
        /// </summary>
        private void BuildTree(List<string> eventHashes)
        {
            Levels = new List<List<MerkleNode>>();
            Leaves = new List<MerkleNode>();

            // Step 1: リーフノードを作成（RFC 6962: 0x00プレフィックス）
            var currentLevel = new List<MerkleNode>();
            for (int i = 0; i < eventHashes.Count; i++)
            {
                var hashBytes = HexToBytes(eventHashes[i]);
                var leafHash = ComputeMerkleHash(hashBytes, isLeaf: true);
                
                var leafNode = new MerkleNode
                {
                    Hash = leafHash,
                    IsLeaf = true,
                    LeafIndex = i
                };
                
                currentLevel.Add(leafNode);
                Leaves.Add(leafNode);
            }
            Levels.Add(new List<MerkleNode>(currentLevel));

            // Step 2: 上位ノードを構築（RFC 6962: 0x01プレフィックス）
            while (currentLevel.Count > 1)
            {
                var nextLevel = new List<MerkleNode>();
                
                for (int i = 0; i < currentLevel.Count; i += 2)
                {
                    MerkleNode left = currentLevel[i];
                    MerkleNode right;
                    
                    if (i + 1 < currentLevel.Count)
                    {
                        right = currentLevel[i + 1];
                    }
                    else
                    {
                        // 奇数の場合は最後のノードを複製
                        right = left;
                    }

                    // 内部ノードハッシュ: H(0x01 || left || right)
                    var combined = new byte[left.Hash.Length + right.Hash.Length];
                    Array.Copy(left.Hash, 0, combined, 0, left.Hash.Length);
                    Array.Copy(right.Hash, 0, combined, left.Hash.Length, right.Hash.Length);
                    
                    var internalHash = ComputeMerkleHash(combined, isLeaf: false);
                    
                    var parentNode = new MerkleNode
                    {
                        Hash = internalHash,
                        Left = left,
                        Right = right,
                        IsLeaf = false
                    };
                    
                    nextLevel.Add(parentNode);
                }
                
                Levels.Add(new List<MerkleNode>(nextLevel));
                currentLevel = nextLevel;
            }

            Root = currentLevel[0];
        }

        /// <summary>
        /// RFC 6962準拠のMerkleハッシュを計算
        /// </summary>
        private byte[] ComputeMerkleHash(byte[] data, bool isLeaf)
        {
            using (var sha256 = SHA256.Create())
            {
                // RFC 6962: リーフは0x00、内部は0x01のプレフィックス
                byte prefix = isLeaf ? LEAF_PREFIX : INTERNAL_PREFIX;
                var prefixedData = new byte[1 + data.Length];
                prefixedData[0] = prefix;
                Array.Copy(data, 0, prefixedData, 1, data.Length);
                
                return sha256.ComputeHash(prefixedData);
            }
        }

        /// <summary>
        /// Merkleルートを取得（16進文字列）
        /// </summary>
        public string GetMerkleRoot()
        {
            if (Root == null)
            {
                throw new InvalidOperationException("ツリーが構築されていません");
            }
            return Root.HashHex;
        }

        /// <summary>
        /// 指定イベントの監査パスを生成
        /// </summary>
        /// <param name="eventId">イベントID</param>
        /// <returns>Merkle包含証明</returns>
        public MerkleInclusionProof GenerateInclusionProof(string eventId)
        {
            if (!_eventIdToIndex.TryGetValue(eventId, out int leafIndex))
            {
                throw new ArgumentException($"イベントID {eventId} が見つかりません");
            }

            return GenerateInclusionProofByIndex(leafIndex, eventId);
        }

        /// <summary>
        /// 指定インデックスの監査パスを生成
        /// </summary>
        /// <param name="leafIndex">リーフインデックス</param>
        /// <param name="eventId">イベントID（オプション）</param>
        /// <returns>Merkle包含証明</returns>
        public MerkleInclusionProof GenerateInclusionProofByIndex(int leafIndex, string eventId = null)
        {
            if (leafIndex < 0 || leafIndex >= Leaves.Count)
            {
                throw new ArgumentException($"無効なインデックス: {leafIndex}");
            }

            var auditPath = new List<AuditPathItem>();
            int index = leafIndex;

            // 各レベルで兄弟ノードを収集
            for (int level = 0; level < Levels.Count - 1; level++)
            {
                var currentLevel = Levels[level];
                int siblingIndex = index ^ 1; // XORで兄弟インデックスを取得

                if (siblingIndex < currentLevel.Count)
                {
                    var sibling = currentLevel[siblingIndex];
                    auditPath.Add(new AuditPathItem
                    {
                        Hash = sibling.HashHex,
                        Position = siblingIndex < index ? "left" : "right"
                    });
                }

                index /= 2;
            }

            return new MerkleInclusionProof
            {
                EventID = eventId ?? _eventHashes[leafIndex],
                EventHash = _eventHashes[leafIndex],
                LeafIndex = leafIndex,
                MerkleRoot = GetMerkleRoot(),
                AuditPath = auditPath,
                TreeSize = Leaves.Count
            };
        }

        /// <summary>
        /// 包含証明を検証
        /// </summary>
        /// <param name="proof">Merkle包含証明</param>
        /// <returns>検証結果</returns>
        public static bool VerifyInclusionProof(MerkleInclusionProof proof)
        {
            if (proof == null || proof.AuditPath == null)
            {
                return false;
            }

            // リーフハッシュを計算
            var currentHash = ComputeMerkleHashStatic(HexToBytes(proof.EventHash), isLeaf: true);

            // 監査パスに沿ってルートまで計算
            foreach (var pathItem in proof.AuditPath)
            {
                var siblingHash = HexToBytes(pathItem.Hash);
                byte[] combined;

                if (pathItem.Position == "left")
                {
                    // 兄弟が左にある場合
                    combined = new byte[siblingHash.Length + currentHash.Length];
                    Array.Copy(siblingHash, 0, combined, 0, siblingHash.Length);
                    Array.Copy(currentHash, 0, combined, siblingHash.Length, currentHash.Length);
                }
                else
                {
                    // 兄弟が右にある場合
                    combined = new byte[currentHash.Length + siblingHash.Length];
                    Array.Copy(currentHash, 0, combined, 0, currentHash.Length);
                    Array.Copy(siblingHash, 0, combined, currentHash.Length, siblingHash.Length);
                }

                currentHash = ComputeMerkleHashStatic(combined, isLeaf: false);
            }

            // 計算したルートと期待されるルートを比較
            var calculatedRoot = BitConverter.ToString(currentHash).Replace("-", "").ToLower();
            return calculatedRoot == proof.MerkleRoot.ToLower();
        }

        /// <summary>
        /// RFC 6962準拠のMerkleハッシュを計算（静的メソッド）
        /// </summary>
        private static byte[] ComputeMerkleHashStatic(byte[] data, bool isLeaf)
        {
            using (var sha256 = SHA256.Create())
            {
                byte prefix = isLeaf ? LEAF_PREFIX : INTERNAL_PREFIX;
                var prefixedData = new byte[1 + data.Length];
                prefixedData[0] = prefix;
                Array.Copy(data, 0, prefixedData, 1, data.Length);
                
                return sha256.ComputeHash(prefixedData);
            }
        }

        /// <summary>
        /// 16進文字列をバイト配列に変換
        /// </summary>
        private static byte[] HexToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex))
            {
                return Array.Empty<byte>();
            }

            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        /// <summary>
        /// ツリーのダンプ情報を取得（デバッグ用）
        /// </summary>
        public string DumpTree()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== Merkle Tree ===");
            sb.AppendLine($"リーフ数: {Leaves.Count}");
            sb.AppendLine($"レベル数: {Levels.Count}");
            sb.AppendLine($"ルート: {GetMerkleRoot()}");
            sb.AppendLine();

            for (int level = Levels.Count - 1; level >= 0; level--)
            {
                sb.AppendLine($"Level {level} ({Levels[level].Count} nodes):");
                foreach (var node in Levels[level])
                {
                    string type = node.IsLeaf ? $"Leaf[{node.LeafIndex}]" : "Internal";
                    sb.AppendLine($"  {type}: {node.HashHex.Substring(0, 16)}...");
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Merkleバッチ（アンカリング用）
    /// </summary>
    public class MerkleBatch
    {
        /// <summary>
        /// バッチID
        /// </summary>
        [JsonPropertyName("BatchID")]
        public string BatchID { get; set; }

        /// <summary>
        /// Merkleルート
        /// </summary>
        [JsonPropertyName("MerkleRoot")]
        public string MerkleRoot { get; set; }

        /// <summary>
        /// イベント数
        /// </summary>
        [JsonPropertyName("EventCount")]
        public int EventCount { get; set; }

        /// <summary>
        /// バッチ開始時刻
        /// </summary>
        [JsonPropertyName("StartTime")]
        public string StartTime { get; set; }

        /// <summary>
        /// バッチ終了時刻
        /// </summary>
        [JsonPropertyName("EndTime")]
        public string EndTime { get; set; }

        /// <summary>
        /// コンプライアンス階層
        /// </summary>
        [JsonPropertyName("ConformanceTier")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ConformanceTier ConformanceTier { get; set; }

        /// <summary>
        /// イベントハッシュ一覧
        /// </summary>
        [JsonPropertyName("EventHashes")]
        public List<string> EventHashes { get; set; }

        /// <summary>
        /// 全イベントの包含証明
        /// </summary>
        [JsonPropertyName("InclusionProofs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<MerkleInclusionProof> InclusionProofs { get; set; }

        /// <summary>
        /// アンカリング済みフラグ
        /// </summary>
        [JsonPropertyName("IsAnchored")]
        public bool IsAnchored { get; set; }

        /// <summary>
        /// アンカーレコード
        /// </summary>
        [JsonPropertyName("AnchorRecord")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public AnchorRecord AnchorRecord { get; set; }
    }
}
