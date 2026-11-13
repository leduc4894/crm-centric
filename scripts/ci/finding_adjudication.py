"""Exact finding adjudication for candidate-owned, maintainer-reviewed MVP CI.

The original capability caller supplies trusted decisions and source bytes.
The MVP adapter also accepts candidate-owned JSON for trusted maintainer
code/policy review; it supplies no independent authorization. Python object shape
or JSON status is not authentication. Neither interface is a security boundary.
Native records are retained in memory only; never publish this result directly.
"""
from collections import Counter
from dataclasses import dataclass, replace
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
    if jar and source is None and hasattr(capability,'reviewed_jar_snapshot'):
        # Explicit MVP tradeoff: a reviewed immutable metadata snapshot is the
        # evidence; no runtime JAR acquisition/recomputation is claimed.
        return capability.reviewed_jar_snapshot(decision)
    return isinstance(source,bytes) and hashlib.sha256(source).hexdigest()==value


def adjudicate(tree, history, documents, context, now, capability=None):
    """Preserve all records. Missing/invalid trust input always yields blocked.

    `capability.lookup(id)` must return an independently authorized Decision for
    the exact reviewed proposal ID; `source_bytes` supplies independent JAR bytes.
    An MVP policy capability can explicitly select reviewed JAR snapshots instead
    of source recomputation; that path requires trusted maintainer review.
    The external host must restrict decisions to the reviewed inventory.
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


def reviewed_content_digest(documents):
    """Full domain-separated SHA256 of exact reviewed paths and document bytes."""
    payload=[[name,hashlib.sha256(content).hexdigest()] for name,content in sorted(documents.items())]
    return hashlib.sha256(b'crm-mvp-reviewed-content-v1\0'+json.dumps(payload,separators=(',',':')).encode()).hexdigest()


def adjudicate_mvp(tree, history, documents, context, now, policy_bytes=None, *, active_only=False):
    """Candidate-owned exact policy adapter for trusted maintainer review.

    Policy contents are NOT authenticated approval. This adapter is appropriate
    only where a trusted maintainer reviews candidate code AND policy changes.
    Missing/pending policy blocks. JAR snapshot review is
    explicit and never claims runtime recomputation. Never publish native records.
    """
    class Policy:
        def __init__(self):
            self.decisions={}
            self.snapshots=set()

        def lookup(self, identifier):
            return self.decisions.get(identifier)

        def source_bytes(self, decision):
            return None

        def reviewed_jar_snapshot(self, decision):
            return decision.finding_id in self.snapshots

    capability=Policy()
    try:
        def invalid_constant(_): raise ValueError('invalid JSON constant')
        policy=json.loads(policy_bytes,object_pairs_hook=_unique,parse_constant=invalid_constant)
        version=policy['schema_version']
        if active_only and version!=2: raise ValueError('inactive policy schema')
        required_top={'schema_version','context','decisions'}
        if version==2: required_top|={'reviewed_content_sha256'}
        if type(version) is not int or version not in (1,2) or set(policy)!=required_top: raise ValueError('invalid policy')
        reviewed_context=Context(**policy['context'])
        if version==2:
            if reviewed_context.candidate!='b1c86f931fdc0a3ab95ea2243c95b35f29637851':
                raise ValueError('invalid review origin')
            # candidate is the historical review origin, never a synthetic commit
            # SHA. Current commit identity remains context.candidate/run evidence.
            paths={entry['path'] for entry in policy['decisions']}
            paths|={'docs/contracts/'+entry['field'][1] for entry in policy['decisions'] if entry['source_kind']=='contract'}
            selected={path:documents[path] for path in paths}
            if (len(policy['decisions'])!=64
                    or not re.fullmatch('[a-f0-9]{64}',policy['reviewed_content_sha256'])
                    or reviewed_content_digest(selected)!=policy['reviewed_content_sha256']):
                raise ValueError('changed reviewed content')
            reviewed_context=replace(reviewed_context,candidate=context.candidate)
        if reviewed_context!=context: raise ValueError('stale policy')
        seen=set()
        required={'finding_id','path','field','start','end','rule','count','source_kind',
                  'document_digest','review_basis','status','owner','expires'}
        for entry in policy['decisions']:
            if set(entry)!=required: raise ValueError('invalid decision')
            if (any(type(entry[key]) is not int for key in ('start','end','count'))
                    or not isinstance(entry['field'],list)
                    or any(not isinstance(part,str) for part in entry['field'])
                    or not isinstance(entry['owner'],str)
                    or (version==2 and (entry['owner']!='maintainer'
                        or entry['expires']!='2027-02-11T00:00:00+00:00'))):
                raise ValueError('invalid decision types')
            identifier=entry['finding_id']
            if not re.fullmatch('[a-f0-9]{64}',identifier) or identifier in seen:
                raise ValueError('ambiguous decision')
            seen.add(identifier)
            if entry['status'] not in ('pending','approved'): raise ValueError('invalid status')
            basis=entry['review_basis']
            if (entry['source_kind'],basis) not in {
                    ('contract','source-recomputed'),('jar','reviewed-jar-snapshot')}:
                raise ValueError('invalid review basis')
            if entry['status']!='approved': continue
            if hashlib.sha256(documents[entry['path']]).hexdigest()!=entry['document_digest']:
                continue
            if basis=='reviewed-jar-snapshot': capability.snapshots.add(identifier)
            capability.decisions[identifier]=Decision(identifier,context,entry['path'],tuple(entry['field']),
                entry['start'],entry['end'],entry['rule'],entry['count'],entry['owner'],
                datetime.fromisoformat(entry['expires']),entry['source_kind'])
    except Exception:
        results=adjudicate(tree,history,documents,context,now)
        for result in results: result['reason']='CI_METADATA_POLICY_INVALID'
        return results
    results=adjudicate(tree,history,documents,context,now,capability)
    for result in results:
        if result['decision']=='verified-metadata':
            snapshot=result['finding_id'] in capability.snapshots
            result['verification_basis']='reviewed-jar-snapshot' if snapshot else 'source-recomputed'
            if snapshot: result['reason']='CI_METADATA_SNAPSHOT_REVIEWED'
    return results
