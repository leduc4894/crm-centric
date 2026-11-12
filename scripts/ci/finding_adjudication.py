"""Local adjudication kernel, deliberately NOT connected to CI or scanner verdicts.

The caller must run outside the candidate's administrative boundary and supply
both a trusted decision lookup and independently available bytes. Python object
shape is not authentication: this interface is a dependency seam, not a security
boundary. No file/JSON/receipt loader or default trusted implementation exists.
Native records are retained in memory only; never publish this result directly.
"""
from collections import Counter
from dataclasses import dataclass
from datetime import datetime
import hashlib
import json
import re
from uuid import uuid4

@dataclass(frozen=True)
class Context:
    candidate: str
    scanner: str
    config: str
    binary: str

@dataclass(frozen=True)
class Decision:
    finding_id: str
    context: Context
    path: str
    field: tuple
    start: int
    end: int
    rule: str
    count: int
    owner: str
    expires: datetime
    source_kind: str


def finding_id(record):
    return hashlib.sha256(json.dumps(['secret',record],sort_keys=True,separators=(',',':')).encode()).hexdigest()


def _unique(pairs):
    result={}
    for key,value in pairs:
        if key in result: raise ValueError('ambiguous JSON')
        result[key]=value
    return result


def _verified(record, decision, context, now, documents, capability, count):
    if not isinstance(decision,Decision) or not isinstance(context,Context): return False
    if (not re.fullmatch('[a-f0-9]{40}',context.candidate)
            or not context.scanner.strip()
            or not re.fullmatch('[a-f0-9]{64}',context.config)
            or not re.fullmatch('[a-f0-9]{64}',context.binary)): return False
    if (decision.context!=context or not decision.owner.strip() or decision.expires<=now
            or decision.finding_id!=finding_id(record) or decision.count!=1 or count!=1
            or decision.path!=record['File'] or decision.rule!='generic-api-key'
            or decision.rule!=record['RuleID'] or decision.start!=record['StartLine']
            or decision.end!=record['EndLine'] or decision.start!=decision.end
            or record.get('Commit') or record['Secret']!='REDACTED'): return False
    jar=decision.path=='docs/ci/candidates/keycloak26/diagnostic-runtime-jars.json'
    provenance=decision.path in {
        'sdks/dotnet/Crm.Client/provenance.json','sdks/dotnet/Platform.Operations.Client/provenance.json',
        'sdks/typescript/crm/provenance.json','sdks/typescript/operations/provenance.json'}
    if jar:
        if decision.source_kind!='jar' or len(decision.field)!=1: return False
        key=decision.field[0]
        if not re.fullmatch(r'lib/(?:[A-Za-z0-9_.-]+/)*[A-Za-z0-9_.-]+\.jar',key): return False
    elif provenance:
        if (decision.source_kind!='contract' or len(decision.field)!=2
                or decision.field[0]!='inputHashes'
                or decision.field[1] not in ('crm-api.openapi.yaml','platform-operations.openapi.yaml')): return False
        key=decision.field[1]
    else: return False
    content=documents[decision.path].decode('utf-8')
    def invalid_constant(_): raise ValueError('invalid JSON constant')
    document=json.loads(content,object_pairs_hook=_unique,parse_constant=invalid_constant)
    # A repeated member name in another object makes line-to-pointer mapping
    # ambiguous; conservatively reject instead of guessing the selected object.
    def occurrences(node):
        if isinstance(node,dict):
            return sum((name==key)+occurrences(child) for name,child in node.items())
        if isinstance(node,list): return sum(occurrences(child) for child in node)
        return 0
    if occurrences(document)!=1: return False
    value=document
    for part in decision.field: value=value[part]
    if not isinstance(value,str) or not re.fullmatch('[a-f0-9]{64}',value): return False
    line=content.splitlines()[decision.start-1]
    # A whole, single JSON member on the reviewed line, with no extra content.
    if not re.fullmatch(r'\s*'+re.escape(json.dumps(key))+r': "'+value+r'",?\s*',line): return False
    match=record['Match']
    if not isinstance(match,str) or match.count('REDACTED')!=1: return False
    if not re.fullmatch(r'[A-Za-z0-9_./-]+": "REDACTED"',match): return False
    if line.count(match.replace('REDACTED',value))!=1: return False
    if jar:
        source=capability.source_bytes(decision)
    else:
        # Contract bytes must belong to the same verified candidate snapshot;
        # the independent caller owns acquisition/identity of this mapping.
        source=documents.get('docs/contracts/'+key)
        # No caller-controlled hash string substitutes for actual source bytes.
    return isinstance(source,bytes) and hashlib.sha256(source).hexdigest()==value


def adjudicate(tree, history, documents, context, now, capability=None):
    """Preserve all records. Missing/invalid trust input always yields blocked.

    `capability.lookup(id)` must return an independently authorized Decision for
    the exact reviewed proposal ID; `source_bytes` supplies independent JAR bytes.
    The external host must restrict decisions to the reviewed 64-ID inventory.
    This module cannot authenticate its host and makes no enforcement claim.
    """
    counts=Counter(finding_id(row) for row in tree)
    results=[]
    for origin,rows in (('tree',tree),('history',history)):
        for record in rows:
            identifier=finding_id(record)
            result={'finding_id':identifier,'record':record,'origin':origin,'decision':'blocked',
                    'reason':'CI_METADATA_NOT_VERIFIED','correlation_id':str(uuid4())}
            try:
                if origin=='tree' and capability is not None:
                    decision=capability.lookup(identifier)
                    if _verified(record,decision,context,now,documents,capability,counts[identifier]):
                        result.update(decision='verified-metadata',reason='CI_METADATA_VERIFIED',
                                      owner=decision.owner,expires=decision.expires.isoformat())
            except Exception:
                # Provider/parser errors are intentionally closed, with bounded
                # reason and correlation; provider errors never expose payloads.
                result['reason']='CI_METADATA_VERIFICATION_FAILED'
            results.append(result)
    return results
