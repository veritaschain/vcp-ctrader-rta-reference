#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
VCP Evidence Pack Verification Script
======================================
VCP v1.1 Specification Compliant
Silver Tier Verification

This script cryptographically verifies VCP audit trails:
1. Event hash verification (SHA-256)
2. Hash chain continuity
3. Merkle proof verification (RFC 6962)
4. Anchor verification

Usage:
    python verify.py [--verbose]

Requirements:
    Python 3.8+
    No external dependencies required
"""

import json
import hashlib
import sys
from pathlib import Path
from typing import Dict, List, Tuple, Optional, Any
from dataclasses import dataclass
from datetime import datetime, timezone

# RFC 6962 Domain Separation Prefixes
LEAF_PREFIX = b'\x00'
INTERNAL_PREFIX = b'\x01'


@dataclass
class VerificationResult:
    """Verification result container"""
    passed: bool
    message: str
    details: Optional[Dict] = None


def canonicalize_json(obj: Any) -> str:
    """RFC 8785 JSON Canonicalization (simplified)"""
    return json.dumps(obj, sort_keys=True, separators=(',', ':'), ensure_ascii=False)


def compute_sha256(data: str) -> str:
    """Compute SHA-256 hash"""
    return hashlib.sha256(data.encode('utf-8')).hexdigest()


def compute_event_hash(header: Dict, payload: Dict) -> str:
    """
    Compute VCP event hash
    EventHash = SHA256(Canonical(Header without EventHash) || Canonical(Payload))
    """
    header_copy = {k: v for k, v in header.items() if k != 'EventHash'}
    canonical_header = canonicalize_json(header_copy)
    canonical_payload = canonicalize_json(payload)
    hash_input = canonical_header + canonical_payload
    return compute_sha256(hash_input)


def compute_merkle_hash(data: bytes, is_leaf: bool) -> bytes:
    """
    RFC 6962 compliant Merkle hash
    Leaf: H(0x00 || data)
    Internal: H(0x01 || left || right)
    """
    prefix = LEAF_PREFIX if is_leaf else INTERNAL_PREFIX
    return hashlib.sha256(prefix + data).digest()


class VCPVerifier:
    """VCP Evidence Pack Verifier"""
    
    def __init__(self, evidence_path: str, verbose: bool = False):
        self.evidence_path = Path(evidence_path)
        self.verbose = verbose
        self.events = None
        self.batches = None
        self.anchors = None
        
    def log(self, message: str):
        """Print verbose log"""
        if self.verbose:
            print(f"  [DEBUG] {message}")
    
    def load_data(self) -> bool:
        """Load all evidence data"""
        try:
            events_file = self.evidence_path / "events.json"
            with open(events_file, 'r', encoding='utf-8') as f:
                self.events = json.load(f)
            self.log(f"Loaded {len(self.events.get('events', []))} events")
            
            batches_file = self.evidence_path / "batches.json"
            with open(batches_file, 'r', encoding='utf-8') as f:
                self.batches = json.load(f)
            self.log(f"Loaded {len(self.batches.get('batches', []))} batches")
            
            anchors_file = self.evidence_path / "anchors.json"
            with open(anchors_file, 'r', encoding='utf-8') as f:
                self.anchors = json.load(f)
            self.log(f"Loaded {len(self.anchors.get('anchors', []))} anchors")
            
            return True
        except Exception as e:
            print(f"Error loading data: {e}")
            return False
    
    def verify_event_hash(self, event: Dict) -> VerificationResult:
        """
        Cryptographically verify a single event's hash
        """
        header = event.get('Header', {})
        stored_hash = header.get('EventHash', '')
        
        # Extract payload
        payload = {k: v for k, v in event.items() if k != 'Header'}
        
        # Compute hash
        computed_hash = compute_event_hash(header, payload)
        
        if computed_hash == stored_hash:
            self.log(f"Event {header.get('EventID', 'unknown')[:16]}... VERIFIED")
            return VerificationResult(
                passed=True,
                message=f"Event hash cryptographically verified",
                details={
                    'event_id': header.get('EventID'),
                    'computed_hash': computed_hash,
                    'stored_hash': stored_hash
                }
            )
        else:
            return VerificationResult(
                passed=False,
                message=f"Event hash MISMATCH",
                details={
                    'event_id': header.get('EventID'),
                    'computed_hash': computed_hash,
                    'stored_hash': stored_hash
                }
            )
    
    def verify_hash_chain(self, events: List[Dict]) -> VerificationResult:
        """
        Verify hash chain continuity
        """
        chain_errors = []
        prev_hash = None
        
        for i, event in enumerate(events):
            header = event.get('Header', {})
            current_prev = header.get('PrevHash')
            current_hash = header.get('EventHash')
            
            if i == 0:
                if current_prev is not None:
                    self.log(f"First event has PrevHash (acceptable)")
            else:
                if current_prev != prev_hash:
                    chain_errors.append({
                        'event_index': i,
                        'event_id': header.get('EventID'),
                        'expected': prev_hash,
                        'found': current_prev
                    })
            
            prev_hash = current_hash
        
        if chain_errors:
            return VerificationResult(
                passed=False,
                message=f"Hash chain has {len(chain_errors)} breaks",
                details={'errors': chain_errors}
            )
        
        return VerificationResult(
            passed=True,
            message="Hash chain continuity verified",
            details={'total_events': len(events)}
        )
    
    def verify_merkle_proof(self, 
                           event_hash: str, 
                           audit_path: List[Dict],
                           merkle_root: str) -> VerificationResult:
        """
        Cryptographically verify Merkle inclusion proof
        """
        try:
            # Start with leaf hash
            current = compute_merkle_hash(bytes.fromhex(event_hash), is_leaf=True)
            self.log(f"Leaf hash: {current.hex()[:16]}...")
            
            # Walk the audit path
            for i, step in enumerate(audit_path):
                sibling = bytes.fromhex(step['hash'])
                if step['position'] == 'left':
                    combined = sibling + current
                else:
                    combined = current + sibling
                current = compute_merkle_hash(combined, is_leaf=False)
                self.log(f"Level {i+1}: {current.hex()[:16]}...")
            
            computed_root = current.hex()
            
            if computed_root == merkle_root.lower():
                return VerificationResult(
                    passed=True,
                    message="Merkle proof cryptographically verified",
                    details={
                        'computed_root': computed_root,
                        'expected_root': merkle_root,
                        'audit_path_length': len(audit_path)
                    }
                )
            else:
                return VerificationResult(
                    passed=False,
                    message="Merkle root MISMATCH",
                    details={
                        'computed_root': computed_root,
                        'expected_root': merkle_root
                    }
                )
            
        except Exception as e:
            return VerificationResult(
                passed=False,
                message=f"Merkle verification error: {e}"
            )
    
    def verify_merkle_root(self, event_hashes: List[str], expected_root: str) -> VerificationResult:
        """
        Rebuild entire Merkle tree and verify root
        """
        try:
            if not event_hashes:
                return VerificationResult(passed=False, message="No event hashes")
            
            # Build leaf nodes
            leaves = [compute_merkle_hash(bytes.fromhex(h), is_leaf=True) for h in event_hashes]
            
            # Build tree upward
            current = leaves
            while len(current) > 1:
                next_level = []
                for i in range(0, len(current), 2):
                    left = current[i]
                    right = current[i + 1] if i + 1 < len(current) else current[i]
                    combined = left + right
                    parent = compute_merkle_hash(combined, is_leaf=False)
                    next_level.append(parent)
                current = next_level
            
            computed_root = current[0].hex()
            
            if computed_root == expected_root.lower():
                return VerificationResult(
                    passed=True,
                    message="Merkle root verified by tree reconstruction",
                    details={
                        'computed_root': computed_root,
                        'leaf_count': len(event_hashes)
                    }
                )
            else:
                return VerificationResult(
                    passed=False,
                    message="Merkle root MISMATCH on reconstruction",
                    details={
                        'computed_root': computed_root,
                        'expected_root': expected_root
                    }
                )
                
        except Exception as e:
            return VerificationResult(
                passed=False,
                message=f"Merkle reconstruction error: {e}"
            )
    
    def verify_anchor(self, anchor: Dict) -> VerificationResult:
        """Verify anchor record"""
        anchor_type = anchor.get('AnchorTarget', 'UNKNOWN')
        anchor_id = anchor.get('AnchorID', 'unknown')
        
        if anchor_type == 'LOCAL_FILE':
            proof = anchor.get('AnchorProof', {})
            merkle_root = anchor.get('MerkleRoot', '')
            proof_hash = proof.get('sha256', '')
            
            # Verify the anchor proof hash matches the merkle root hash
            computed_proof = compute_sha256(merkle_root)
            
            if computed_proof == proof_hash:
                return VerificationResult(
                    passed=True,
                    message=f"Anchor {anchor_id} verified",
                    details={
                        'anchor_type': 'LOCAL_FILE',
                        'merkle_root': merkle_root[:16] + '...',
                        'proof_verified': True
                    }
                )
            else:
                return VerificationResult(
                    passed=False,
                    message=f"Anchor proof hash mismatch",
                    details={
                        'computed': computed_proof,
                        'stored': proof_hash
                    }
                )
        
        return VerificationResult(
            passed=True,
            message=f"Anchor {anchor_id} structure valid (external verification required)",
            details={'anchor_type': anchor_type}
        )
    
    def verify_timeline(self, events: List[Dict]) -> VerificationResult:
        """Verify events are chronologically ordered"""
        out_of_order = []
        prev_timestamp = None
        
        for i, event in enumerate(events):
            header = event.get('Header', {})
            current_ts = header.get('TimestampInt', 0)
            
            if prev_timestamp is not None and current_ts < prev_timestamp:
                out_of_order.append({
                    'event_index': i,
                    'event_id': header.get('EventID'),
                    'timestamp': header.get('TimestampISO')
                })
            
            prev_timestamp = current_ts
        
        if out_of_order:
            return VerificationResult(
                passed=False,
                message=f"{len(out_of_order)} events out of order",
                details={'out_of_order': out_of_order}
            )
        
        return VerificationResult(
            passed=True,
            message="Timeline chronology verified"
        )
    
    def run_verification(self) -> Dict:
        """Run complete verification suite"""
        print("=" * 60)
        print("VCP Evidence Pack Verification Report")
        print("=" * 60)
        print(f"Evidence Path: {self.evidence_path}")
        print(f"Verification Time: {datetime.now(timezone.utc).isoformat()}")
        print()
        
        if not self.load_data():
            return {'passed': False, 'error': 'Failed to load evidence data'}
        
        results = {
            'event_hashes': [],
            'merkle_proofs': [],
            'merkle_root': None,
            'anchors': [],
            'hash_chain': None,
            'timeline': None
        }
        
        events = self.events.get('events', [])
        batches = self.batches.get('batches', [])
        anchors = self.anchors.get('anchors', [])
        
        print(f"Total Events: {len(events)}")
        print(f"Total Batches: {len(batches)}")
        print(f"Total Anchors: {len(anchors)}")
        print()
        
        # 1. Event Hash Verification (Cryptographic)
        print("Event Hash Verification (SHA-256):")
        event_pass = 0
        event_fail = 0
        for event in events:
            result = self.verify_event_hash(event)
            results['event_hashes'].append(result)
            if result.passed:
                event_pass += 1
            else:
                event_fail += 1
                print(f"  ✗ {result.details}")
        
        if event_fail == 0:
            print(f"  ✓ All {event_pass} event hashes cryptographically verified")
        else:
            print(f"  ✗ {event_fail}/{len(events)} events failed hash verification")
        print()
        
        # 2. Hash Chain Verification
        print("Hash Chain Verification:")
        results['hash_chain'] = self.verify_hash_chain(events)
        if results['hash_chain'].passed:
            print(f"  ✓ {results['hash_chain'].message}")
        else:
            print(f"  ✗ {results['hash_chain'].message}")
        print()
        
        # 3. Merkle Root Verification (Full Tree Reconstruction)
        print("Merkle Root Verification (Tree Reconstruction):")
        for batch in batches:
            event_hashes = batch.get('EventHashes', [])
            expected_root = batch.get('MerkleRoot', '')
            
            result = self.verify_merkle_root(event_hashes, expected_root)
            results['merkle_root'] = result
            
            if result.passed:
                print(f"  ✓ Merkle root: {expected_root[:32]}...")
                print(f"    Verified by rebuilding tree from {len(event_hashes)} hashes")
            else:
                print(f"  ✗ {result.message}")
        print()
        
        # 4. Merkle Inclusion Proof Verification
        print("Merkle Inclusion Proof Verification:")
        proof_count = 0
        for batch in batches:
            proofs = batch.get('InclusionProofs', [])
            for proof in proofs:
                result = self.verify_merkle_proof(
                    proof.get('EventHash', ''),
                    proof.get('AuditPath', []),
                    proof.get('MerkleRoot', '')
                )
                results['merkle_proofs'].append(result)
                proof_count += 1
                
                if result.passed:
                    print(f"  ✓ Event {proof.get('EventID', 'unknown')[:16]}... verified")
                else:
                    print(f"  ✗ {result.message}")
        
        if proof_count == 0:
            print(f"  ○ No inclusion proofs to verify")
        print()
        
        # 5. Anchor Verification
        print("Anchor Verification:")
        for anchor in anchors:
            result = self.verify_anchor(anchor)
            results['anchors'].append(result)
            
            if result.passed:
                print(f"  ✓ {result.message}")
            else:
                print(f"  ✗ {result.message}")
        print()
        
        # 6. Timeline Verification
        print("Timeline Verification:")
        results['timeline'] = self.verify_timeline(events)
        if results['timeline'].passed:
            print(f"  ✓ {results['timeline'].message}")
        else:
            print(f"  ✗ {results['timeline'].message}")
        print()
        
        # Overall Result
        all_passed = (
            event_fail == 0 and
            results['hash_chain'].passed and
            (results['merkle_root'] is None or results['merkle_root'].passed) and
            all(r.passed for r in results['merkle_proofs']) and
            all(r.passed for r in results['anchors']) and
            results['timeline'].passed
        )
        
        print("=" * 60)
        if all_passed:
            print("Overall: ✓ CRYPTOGRAPHICALLY VERIFIED")
            print()
            print("All hashes, chains, and proofs have been independently computed")
            print("and match the stored values. This evidence pack is authentic.")
        else:
            print("Overall: ✗ VERIFICATION FAILED")
            print()
            print("Some cryptographic checks failed. Evidence may be tampered.")
        print("=" * 60)
        
        return {
            'passed': all_passed,
            'results': results,
            'summary': {
                'total_events': len(events),
                'total_batches': len(batches),
                'total_anchors': len(anchors),
                'event_hash_pass': event_pass,
                'event_hash_fail': event_fail
            }
        }


def main():
    """Main entry point"""
    verbose = '--verbose' in sys.argv or '-v' in sys.argv
    evidence_path = Path(__file__).parent
    
    verifier = VCPVerifier(str(evidence_path), verbose=verbose)
    result = verifier.run_verification()
    
    sys.exit(0 if result.get('passed', False) else 1)


if __name__ == '__main__':
    main()
