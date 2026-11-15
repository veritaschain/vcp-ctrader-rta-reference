"""Offline checks for the exact Python script emitted by the C# exporter."""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
DOTNET = os.environ.get('VCP_DOTNET') or shutil.which('dotnet')


def exported_script():
    source = (ROOT / 'src/VCP.CTrader.RTA/VCPEvidencePack.cs').read_text(encoding='utf-8')
    match = re.search(r'private string GenerateVerificationScript\(\)\s*\{\s*return @"((?:""|[^"])*)";', source)
    if match is None:
        raise AssertionError('Cannot extract the actual C# verbatim-string export template')
    return match.group(1).replace('""', '"')


def synthetic_batch():
    # An explicit three-leaf tree checks the legacy duplicated-odd-leaf boundary.
    hashes = ['11' * 32, '22' * 32, '33' * 32]
    leaves = [hashlib.sha256(b'\x00' + bytes.fromhex(value)).digest() for value in hashes]
    left = hashlib.sha256(b'\x01' + leaves[0] + leaves[1]).digest()
    right = hashlib.sha256(b'\x01' + leaves[2] + leaves[2]).digest()
    root = hashlib.sha256(b'\x01' + left + right).hexdigest()
    return {'BatchID': 'synthetic-batch', 'EventHashes': hashes, 'EventCount': 3, 'MerkleRoot': root}


class ExportCheckerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.script = self.directory / 'verify.py'
        self.script.write_text(exported_script(), encoding='utf-8')
        self.batch_file = self.directory / 'batches.json'

    def run_cli(self, *args):
        return subprocess.run([sys.executable, str(self.script), *map(str, args)],
                              cwd=ROOT, capture_output=True, text=True)

    def write(self, batches, encoding='utf-8'):
        self.batch_file.write_text(json.dumps(batches), encoding=encoding)

    def assert_code(self, code, *args):
        result = self.run_cli(*args)
        self.assertEqual(result.returncode, code, result.stdout + result.stderr)
        self.assertNotIn('Traceback', result.stderr)
        return result

    def test_local_three_leaf_root_matches_with_explicit_scope(self):
        self.write([synthetic_batch()])
        result = self.assert_code(0)
        self.assertIn('MATCH (local hashes only; not authenticated)', result.stdout)
        self.assertIn('NOT VERIFIED: event payloads, hash chains, inclusion proofs, signatures,', result.stdout)

    def test_utf8_bom_export_is_supported(self):
        self.write([synthetic_batch()], encoding='utf-8-sig')
        self.assert_code(0)

    def test_one_leaf_root_uppercase_hex_and_integer_count(self):
        batch = synthetic_batch()
        batch['EventHashes'] = ['AB' * 32]
        batch['EventCount'] = 1
        batch['MerkleRoot'] = hashlib.sha256(b'\x00' + bytes.fromhex('AB' * 32)).hexdigest().upper()
        self.write([batch])
        self.assert_code(0)
        batch['EventCount'] = True
        self.write([batch])
        self.assert_code(1)

    def test_a_mismatch_anywhere_in_the_batch_list_is_invalid(self):
        valid = synthetic_batch()
        invalid = dict(valid, BatchID='other', MerkleRoot='00' * 32)
        for batches in ([valid, invalid], [invalid, valid]):
            with self.subTest(order=[item['BatchID'] for item in batches]):
                self.write(batches)
                self.assertIn('Batch-root check: INVALID', self.assert_code(1).stdout)

    def test_empty_list_and_empty_batch_are_indeterminate(self):
        empty = dict(synthetic_batch(), EventHashes=[], EventCount=0)
        for batches in ([], [empty]):
            with self.subTest(batches=batches):
                self.write(batches)
                self.assertIn('INDETERMINATE', self.assert_code(2).stdout)

    def test_invalid_result_takes_precedence_over_empty_batch(self):
        empty = dict(synthetic_batch(), EventHashes=[], EventCount=0)
        bad = dict(synthetic_batch(), BatchID='bad', MerkleRoot='00' * 32)
        self.write([empty, bad])
        self.assert_code(1)

    def test_missing_input_is_indeterminate(self):
        self.assertIn('unavailable', self.assert_code(2).stdout)
        self.assertIn('unavailable', self.assert_code(2, self.directory / 'missing').stdout)

    def test_malformed_json_and_invalid_utf8_do_not_escape(self):
        duplicate = json.dumps([synthetic_batch()]).replace('{', '{"EventCount":3,', 1).encode()
        for data in (b'{', b'\xff', b'null', b'{}', b'[null]', duplicate):
            with self.subTest(data=data):
                self.batch_file.write_bytes(data)
                self.assert_code(1)

    def test_nonfinite_json_numbers_in_unused_fields_are_invalid(self):
        for number in ('NaN', 'Infinity', '-Infinity', '1e400', '-1e400'):
            with self.subTest(number=number):
                encoded = json.dumps([synthetic_batch()]).replace('{', '{"unused":' + number + ',', 1)
                self.batch_file.write_text(encoded, encoding='utf-8')
                self.assert_code(1)

    def test_finite_decimal_metadata_does_not_block_local_checks(self):
        self.write([dict(synthetic_batch(), unused=1e100)])
        self.assert_code(0)

    def test_batch_field_types_hash_lengths_and_counts_are_checked(self):
        changes = ({'EventCount': 3.0}, {'EventCount': True}, {'EventCount': 2},
                   {'EventCount': '3'}, {'EventHashes': None}, {'EventHashes': ['01'] * 3},
                   {'MerkleRoot': None}, {'MerkleRoot': 'zz' * 32}, {'BatchID': ''})
        for fields in changes:
            with self.subTest(fields=fields):
                self.write([dict(synthetic_batch(), **fields)])
                self.assert_code(1)
        batch = synthetic_batch()
        del batch['EventCount']
        self.write([batch])
        self.assert_code(1)

    def test_duplicate_batch_ids_are_invalid(self):
        self.write([synthetic_batch(), synthetic_batch()])
        self.assert_code(1)

    def test_explicit_directory_is_honored(self):
        self.write([dict(synthetic_batch(), MerkleRoot='00' * 32)])
        alternate = self.directory / 'alternate'
        alternate.mkdir()
        (alternate / 'batches.json').write_text(json.dumps([synthetic_batch()]), encoding='utf-8')
        self.assert_code(0, alternate)
        self.assert_code(1)

    def test_help_and_usage_errors_do_not_run_checks(self):
        self.assertNotIn('Batch-root check:', self.assert_code(0, '--help').stdout)
        for args in (('--unknown',), (self.directory, 'extra')):
            with self.subTest(args=args):
                result = self.assert_code(2, *args)
                self.assertIn('usage:', result.stderr)
                self.assertNotIn('Scope:', result.stdout)

    def test_event_and_anchor_files_are_outside_the_declared_check_scope(self):
        self.write([synthetic_batch()])
        for name in ('events.json', 'anchors.json'):
            (self.directory / name).write_text('not valid evidence', encoding='utf-8')
        result = self.assert_code(0)
        self.assertIn('NOT VERIFIED:', result.stdout)
        self.assertIn('external anchors, origin, completeness or VCP conformity.', result.stdout)

    def test_direct_root_api_returns_false_for_empty_or_malformed_input(self):
        namespace = {'__name__': 'export_checker'}
        exec(compile(exported_script(), 'exported_verify.py', 'exec'), namespace)
        check = namespace['verify_merkle_root']
        valid = synthetic_batch()
        self.assertTrue(check(valid['EventHashes'], valid['MerkleRoot']))
        for hashes, root in (([], valid['MerkleRoot']), (None, valid['MerkleRoot']),
                             (['01'], valid['MerkleRoot']), (valid['EventHashes'], None)):
            self.assertFalse(check(hashes, root))


class HistoricalPreservationTests(unittest.TestCase):
    def test_hash_covered_sample_files_are_unchanged(self):
        sample = ROOT / 'evidence/sample_pack_2025Q1'
        manifest = json.loads((sample / 'hash_manifest.json').read_text(encoding='utf-8'))
        for path, expected in manifest['files'].items():
            with self.subTest(path=path):
                content = (sample / path).read_bytes()
                self.assertEqual(hashlib.sha256(content).hexdigest(), expected['sha256'])
                self.assertEqual(len(content), expected['size_bytes'])


@unittest.skipUnless(DOTNET, 'Set VCP_DOTNET or install the .NET 8 SDK for the real export smoke test')
class DotnetExportTests(unittest.TestCase):
    def test_actual_export_script_and_bom_round_trip(self):
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp)
            build = subprocess.run([DOTNET, 'run', '--project', str(ROOT / 'tests/ExportSmoke'),
                                    '--configuration', 'Release', '--', str(output)],
                                   cwd=ROOT, capture_output=True, text=True, timeout=120)
            self.assertEqual(build.returncode, 0, build.stdout + build.stderr)
            exported = output / 'with-batch'
            self.assertEqual((exported / 'verify.py').read_text(encoding='utf-8-sig'), exported_script())
            self.assertTrue((exported / 'batches.json').read_bytes().startswith(b'\xef\xbb\xbf'))
            result = subprocess.run([sys.executable, str(exported / 'verify.py')],
                                    cwd=ROOT, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('not authenticated', result.stdout)
            empty = subprocess.run([sys.executable, str(output / 'without-batch/verify.py')],
                                   cwd=ROOT, capture_output=True, text=True)
            self.assertEqual(empty.returncode, 2, empty.stdout + empty.stderr)
            data = json.loads((exported / 'batches.json').read_text(encoding='utf-8-sig'))
            data[0]['MerkleRoot'] = '00' * 32
            (exported / 'batches.json').write_text(json.dumps(data), encoding='utf-8-sig')
            mismatch = subprocess.run([sys.executable, str(exported / 'verify.py')],
                                      cwd=ROOT, capture_output=True, text=True)
            self.assertEqual(mismatch.returncode, 1, mismatch.stdout + mismatch.stderr)


if __name__ == '__main__':
    unittest.main()
