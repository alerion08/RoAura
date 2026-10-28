"""Synthetic offline replay checks; no private capture fixtures."""
import struct
import unittest
from inspect_springro_status_samples import inspect


def sample():
    own = 0x001E1234
    status = struct.pack('<HHIBIIiii', 0x0983, 47410, own ^ 0xABCD, 1, 180000, 180000, 0, 0, 0)
    skill = struct.pack('<HHIIIB', 0x09CB, 138, 3, own, own, 1)
    return dict(schema=1, selfAid=own, skillId=138, statusOffset=100, skillOffset=129,
                statusHex=status.hex(), optionHex='', skillHex=skill.hex())


class ReplayTests(unittest.TestCase):
    def test_reproduces_unknown_standard_actor_without_inventing_id(self):
        result = inspect(sample())
        self.assertFalse(result['standardActorMatchesSelf'])
        self.assertEqual(47410, result['wireStatusCandidate'])
        self.assertEqual(180000, result['leftMsCandidate'])
        self.assertNotIn('selfAid', result)
        self.assertEqual(64, len(result['sha256']))

    def test_inconsistent_and_incomplete_samples_fail(self):
        for field, value in [('schema', 2), ('skillOffset', 130), ('statusHex', '8309'),
                             ('selfAid', 123), ('skillId', 51), ('optionHex', '00' * 15)]:
            with self.subTest(field=field), self.assertRaises(ValueError):
                inspect(sample() | {field: value})

    def test_failed_or_other_target_ack_fails(self):
        data = sample()
        skill = bytearray.fromhex(data['skillHex'])
        skill[-1] = 0
        with self.assertRaises(ValueError):
            inspect(data | {'skillHex': skill.hex()})
        skill[-1] = 1
        struct.pack_into('<I', skill, 8, data['selfAid'] + 1)
        with self.assertRaises(ValueError):
            inspect(data | {'skillHex': skill.hex()})


if __name__ == '__main__':
    unittest.main()
