"""Rebuild an artifact from allowed fields; validation is not publication.

Native diagnostic bodies and arbitrary report extensions never survive this
boundary. Existing scanner/known-secret guards remain required at their producers;
this schema projection does not claim to detect secrets hidden in package names.
"""
from dataclasses import asdict
from datetime import datetime
import hashlib
import json
import os
import re
from scripts.ci.summary import ReportError, parse_cases


def integer(value):
    if type(value) is not int or value<0: raise ReportError()
    return value


def digest(value):
    if not isinstance(value,str) or not re.fullmatch('[0-9a-f]{64}',value): raise ReportError()
    return value


def findings(rows):
    result=[]
    for row in rows:
        kind=row['kind'];verdict=row['verdict']
        if kind not in ('secret','vulnerability','sast','license') or verdict not in ('blocked','pass','incomplete','review-required'):
            raise ReportError()
        result.append({'finding_id':digest(row['finding_id']),'kind':kind,'verdict':verdict})
    return result


def identity(raw,expected):
    if any(raw.get(key)!=value for key,value in asdict(expected).items()): raise ReportError()
    return asdict(expected)


def image_identity(raw):
    from scripts.ci.image_identity import ImageIdentity,validate
    return validate(ImageIdentity(**{key:raw[key] for key in ImageIdentity.__dataclass_fields__}))


def image_record(raw):
    from scripts.ci.sbom import normalize_sbom
    tested=image_identity(raw['testedIdentity']);scan=raw['scan']
    scanned=image_identity(scan['identity'])
    if tested!=scanned or raw['role'] not in ('runtime','api','migrator','postgres','keycloak'):
        raise ReportError()
    status=scan['status']
    if status not in ('pass','blocked','incomplete'): raise ReportError()
    result={'identity':asdict(scanned),'status':status,'packages':integer(scan['packages']),
            'reason_token':{'pass':'CI_IMAGE_SCAN_OK','blocked':'CI_IMAGE_FINDINGS','incomplete':'CI_IMAGE_SCAN_INCOMPLETE'}[status],
            'correlation_id':correlation(),'sbomSha256':None}
    if 'findings' in scan: result['findings']=findings(scan['findings'])
    if 'sbom' in scan:
        sbom=normalize_sbom(scan['sbom'],scanned,scan['sbom']['metadata']['tools']['components'][0]['version'])
        result.update(sbom=sbom,sbomSha256=hashlib.sha256(json.dumps(sbom,sort_keys=True,separators=(',',':')).encode()).hexdigest())
        for key in ('databaseUpdatedAt','javaDatabaseUpdatedAt'):
            # Reconstruct datetime text; arbitrary suffixes/messages are rejected.
            result[key]=datetime.fromisoformat(scan[key].replace('Z','+00:00')).isoformat()
    return {'role':raw['role'],'testedIdentity':asdict(tested),'scan':result}


def source_record(raw,expected):
    from scripts.ci.sbom import source_sbom
    from urllib.parse import unquote
    # Rebuild packages/purls/tool metadata/properties through the producer's
    # canonical inventory function; never copy arbitrary nested SBOM dictionaries.
    packages={}
    targets={'npm':'package-lock.json','nuget':'packages.lock.json','pypi':'requirements.txt'}
    for item in raw['sbom']['components']:
        purl=item['purl']
        match=re.fullmatch(r'pkg:(npm|nuget|pypi)/(.+)@([^@]+)',purl)
        if not match or unquote(match[2])!=item['name'] or unquote(match[3])!=item['version']:
            raise ReportError()
        packages.setdefault(targets[match[1]],set()).add((item['name'],item['version']))
    sbom=source_sbom(packages,expected,'1.0.0')
    status=raw['status']
    if status not in ('inventory-only','incomplete'): raise ReportError()
    gaps=raw['coverageGaps']
    if not isinstance(gaps,list) or any(gap not in ('CI_TOOL_DEPENDENCY_COVERAGE','CI_UNLOCKED_DEPENDENCIES','CI_ECOSYSTEM_COVERAGE') for gap in gaps):
        raise ReportError()
    return {'identity':identity(raw['identity'],expected),'status':status,'coverageGaps':list(gaps),
            'lockCount':integer(raw['lockCount']),'sbom':sbom,
            'sbomSha256':hashlib.sha256(json.dumps(sbom,sort_keys=True,separators=(',',':')).encode()).hexdigest()}


def correlation():
    import uuid
    return str(uuid.uuid4())


def normalized_report(raw,parsed,expected,mode="hardening"):
    """Return a new bounded schema; never mutate or return the parsed input."""
    job=parsed.job;status=parsed.status
    tools={}
    for key in ('node','npm','python','dotnet','docker','compose','semgrep','gitleaks','trivy'):
        if key in raw['tools']:
            value=raw['tools'][key]
            if not isinstance(value,str) or not re.fullmatch('[0-9]+(?:[.][0-9]+){0,3}',value): raise ReportError()
            tools[key]=value
    if not tools: raise ReportError()
    for key,variable in (('runner_image','ImageOS'),('runner_version','ImageVersion')):
        value=os.environ.get(variable,'local')
        if not re.fullmatch('[A-Za-z0-9_.+-]{1,128}',value): raise ReportError()
        tools[key]=value
    suites=[]
    for suite in raw['suites']:
        cases=parse_cases(suite['format'],suite['report']) if 'format' in suite else suite['tests']
        suites.append({'name':suite['name'],'tests':[{'id':case['id'],'outcome':case['outcome']} for case in cases]})
    allowed_reasons={'CI_JOB_COMPLETE','CI_JOB_FAILED','CI_COMMAND_FAILED','CI_COMMAND_TIMEOUT',
        'CI_COMMAND_CANCELLED','CI_PREREQUISITE_MISSING','CI_TOOLCHAIN_MISMATCH','CI_JOB_INPUT_INVALID',
        'CI_CANDIDATE_UNAVAILABLE','CI_IMAGE_EVIDENCE_INCOMPLETE','CI_SCAN_INCOMPLETE','CI_SCAN_FINDINGS',
        'CI_SCAN_OK','CI_DISCOVERY_INCOMPLETE','CI_TEST_EVIDENCE_FAILED','CI_UNEXPECTED_SKIP',
        'CI_REPORT_STATUS_MISMATCH','CI_REPORT_INVALID','CI_REPORT_IDENTITY_MISMATCH','CI_REPORT_FORMAT_UNSUPPORTED'}
    reason=raw.get('reason_token')
    if reason not in allowed_reasons: reason='CI_JOB_COMPLETE' if status=='success' else 'CI_JOB_FAILED'
    result={'job':job,'identity':asdict(expected),'command_id':job,'status':status,
            'tools':tools,'artifacts':[],'elapsed_seconds':raw['elapsed_seconds'],'suites':suites,
            'passed':parsed.passed,'failed':parsed.failed,'skipped':parsed.skipped,'total':parsed.total,
            'reason_token':reason,'correlation_id':correlation()}
    if mode=='mvp' and job in ('backend','compose','web-browser'):
        result['image_hardening']={'status':'not-evaluated'}
    if mode=='hardening' and job in ('backend','compose','web-browser') and 'image_evidence' in raw:
        result['image_evidence']=[image_record(row) for row in raw['image_evidence']]
    if job=='security':
        if 'security_evidence' in raw:
            scan=raw['security_evidence'];scan_status=scan['status']
            if scan_status not in ('pass','blocked','incomplete'): raise ReportError()
            result['security_evidence']={'identity':identity(scan['identity'],expected),'status':scan_status,
                'scanned_count':integer(scan['scanned_count']),'findings':findings(scan['findings']),
                'reason_token':{'pass':'CI_SCAN_OK','blocked':'CI_SCAN_FINDINGS','incomplete':'CI_SCAN_INCOMPLETE'}[scan_status],
                'correlation_id':correlation()}
        if 'source_evidence' in raw: result['source_evidence']=source_record(raw['source_evidence'],expected)
    return result
