"""Candidate-owned review policy: tests never constitute approval."""
import copy
from dataclasses import asdict
from datetime import timedelta
import hashlib
import json
import unittest
import test_finding_adjudication as fixtures
NOW=fixtures.NOW
from scripts.ci import finding_adjudication as kernel

class MvpPolicyTests(unittest.TestCase):
    def setUp(self):
        fixtures.AdjudicationTests.setUp(self)
        self.policy={'schema_version':1,'context':asdict(self.context),'decisions':[{
            'finding_id':self.decision.finding_id,'path':self.path,'field':['inputHashes',self.field],
            'start':3,'end':3,'rule':'generic-api-key','count':1,'source_kind':'contract',
            'document_digest':hashlib.sha256(self.document).hexdigest(),
            'review_basis':'source-recomputed','status':'approved','owner':'trusted-maintainer','expires':(NOW+timedelta(days=1)).isoformat()}]}
    def evaluate(self, policy=None, rows=None, history=(), document=None, context=None):
        self.assertTrue(hasattr(kernel,'adjudicate_mvp'), 'candidate-owned exact policy adapter missing')
        docs={self.path:self.document if document is None else document,'docs/contracts/'+self.field:self.source}
        return kernel.adjudicate_mvp([self.row] if rows is None else rows,history,docs,
            context or self.context,NOW,json.dumps(self.policy if policy is None else policy).encode())
    def test_reviewed_contract_retains_record_but_history_and_new_findings_block(self):
        other=dict(self.row,RuleID='another-rule')
        results=self.evaluate(rows=[self.row,other],history=[self.row])
        self.assertEqual(['verified-metadata','blocked','blocked'],[r['decision'] for r in results])
        self.assertEqual([self.row,other,self.row],[r['record'] for r in results])
    def test_pending_missing_owner_expired_changed_source_and_document_block(self):
        for key,value in [('status','pending'),('owner',''),('expires',NOW.isoformat()),
                          ('document_digest','f'*64),('source_kind','jar')]:
            policy=copy.deepcopy(self.policy);policy['decisions'][0][key]=value
            self.assertEqual('blocked',self.evaluate(policy)[0]['decision'])
        self.assertEqual('blocked',self.evaluate(document=self.document+b'\n')[0]['decision'])
        self.source=b'changed source'
        self.assertEqual('blocked',self.evaluate()[0]['decision'])
    def test_jar_snapshot_review_requires_exact_document_and_explicit_basis(self):
        self.path='docs/ci/candidates/keycloak26/diagnostic-runtime-jars.json'
        self.field='lib/app/keycloak.jar'
        self.document=json.dumps({self.field:self.digest},indent=2).encode()
        self.row.update(File=self.path,StartLine=2,EndLine=2,Match=self.field+'": "REDACTED"')
        entry=self.policy['decisions'][0]
        entry.update(finding_id=kernel.finding_id(self.row),path=self.path,field=[self.field],
                     start=2,end=2,source_kind='jar',review_basis='reviewed-jar-snapshot',
                     document_digest=hashlib.sha256(self.document).hexdigest())
        result=self.evaluate()[0]
        self.assertEqual('verified-metadata',result['decision'])
        self.assertEqual('reviewed-jar-snapshot',result.get('verification_basis'))
        self.assertEqual('CI_METADATA_SNAPSHOT_REVIEWED',result['reason'])
        self.assertEqual('blocked',self.evaluate(document=self.document+b'\n')[0]['decision'])
        for basis in ('source-recomputed','unknown'):
            entry['review_basis']=basis
            self.assertEqual('blocked',self.evaluate()[0]['decision'])

    def test_stale_duplicate_malformed_and_unknown_policy_fail_closed(self):
        for mutation in ('duplicate','context','unknown'):
            policy=copy.deepcopy(self.policy)
            if mutation=='duplicate':policy['decisions']*=2
            elif mutation=='context':policy['context']['candidate']='c'*40
            else:policy['unexpected']='ignored?'
            self.assertEqual('blocked',self.evaluate(policy)[0]['decision'])
        self.assertEqual('blocked',self.evaluate(rows=[self.row,self.row])[0]['decision'])
        for raw in (None,b'{',b'{"schema_version":1,"schema_version":1}',b'{"x":NaN}'):
            results=kernel.adjudicate_mvp([self.row],[],{},self.context,NOW,raw)
            self.assertEqual('blocked',results[0]['decision'])
