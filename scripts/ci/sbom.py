"""Small machine-readable inventory; scanner descriptions/properties never exported.

Raw SBOM stays private. Names/versions use a strict grammar; arbitrary matched
strings, paths, metadata, URLs and environment are never copied to artifacts.
"""
import re
from dataclasses import asdict
from scripts.ci.scan import ScanError


def normalize_sbom(raw, identity, version):
    from scripts.ci.image_identity import validate
    validate(identity)
    try:
        if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+',version): raise ScanError()
        if raw['bomFormat']!='CycloneDX' or not raw['components']: raise ScanError()
        components=[]
        for component in raw['components']:
            if component.get('type')=='application' and not component.get('version'): continue
            name=component['name']; release=component['version']
            if not re.fullmatch('[A-Za-z0-9@_.+:/~-]{1,256}',name) or not re.fullmatch('[A-Za-z0-9_.+:/~!()=-]{1,256}',release): raise ScanError()
            components.append({'type':'library','name':name,'version':release})
        if not components: raise ScanError()
        return {'bomFormat':'CycloneDX','specVersion':'1.6','version':1,
                'components':components,
                'metadata':{'tools':{'components':[{'type':'application','name':'trivy','version':version}]},
                            'properties':[{'name':'crm:image:'+key,'value':value} for key,value in asdict(identity).items() if value is not None]}}
    except (KeyError,TypeError,ValueError,AttributeError,RecursionError): raise ScanError() from None


def require_no_secrets(payload, secrets):
    """Check known test/runtime sentinels without returning matched values."""
    if isinstance(payload,dict):
        for key,value in payload.items():
            require_no_secrets(key,secrets); require_no_secrets(value,secrets)
    elif isinstance(payload,(list,tuple)):
        for value in payload: require_no_secrets(value,secrets)
    elif isinstance(payload,str):
        if any(secret and secret in payload for secret in secrets): raise ScanError()
