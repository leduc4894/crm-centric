"""Maintainer review authorizes identity delta only, never semantic success."""
import copy
import unittest
from scripts.ci import contract_review
class ContractReviewTests(unittest.TestCase):
    def test_exact_delta_and_base_only(self):
        old={p:v[0] for p,v in contract_review.DELTA.items() if v[0] is not None}
        new={p:v[1] for p,v in contract_review.DELTA.items()}
        old['unchanged']='same';new['unchanged']='same'
        self.assertTrue(contract_review.approved(old,new,contract_review.BASE))
        self.assertFalse(contract_review.approved(old,new,'0'*40))
        self.assertFalse(contract_review.approved(old,dict(new,extra='changed'),contract_review.BASE))
        self.assertFalse(contract_review.approved(old,dict(new,unchanged='changed'),contract_review.BASE))
        changed=copy.deepcopy(new);changed[next(iter(contract_review.DELTA))]='0'*64
        self.assertFalse(contract_review.approved(old,changed,contract_review.BASE))
        self.assertFalse(contract_review.approved(old,new,contract_review.BASE,now=2000000000))

    def test_default_gate_keeps_review_blocked_and_mvp_runs_identity_review(self):
        from scripts.contracts import compatibility as gate
        from pathlib import Path
        root=Path(__file__).resolve().parents[2]
        with self.assertRaises(gate.GateError):
            gate.check_baseline_configuration(root,contract_review.BASE)
        reviewed=gate.check_baseline_configuration(root,contract_review.BASE,mvp_review=True)
        self.assertFalse(reviewed['independentVerification'])
        self.assertFalse(reviewed['dualPolicyEvaluation'])

    def test_mvp_clean_export_forwards_opt_in_and_hardening_does_not(self):
        from scripts.ci.jobs import plan3_commands
        from scripts.ci.summary import RunIdentity
        identity=RunIdentity('test','1','a'*40,'b'*40,None)
        for mode in ('mvp','hardening'):
            commands=plan3_commands(identity,mode)
            export=next(c for c in commands if 'tests/Plan3/clean-export.sh' in c)
            self.assertEqual(mode=='mvp','--mvp-reviewed-policy' in export)
