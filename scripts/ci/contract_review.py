"""Candidate-owned MVP identity review, explicitly approved by maintainer.

This is not independent verification or a semantic verdict. The actual gate
still compares the true event base contracts to candidate with pinned tools,
security checks, SDK generation and historical consumers. Independent dual-policy
execution remains pre-production hardening; no capability/attestation is forged.
"""
import time
BASE='c4a5ef40a34daded023d5e5121c0462e94cc1487'
# Remote main differs from the local reviewed base only in three documentation
# files; its complete baseline policy inventory has identical content hashes.
REMOTE_BASE='df993ae4ff44510a2ecfc861bb277a3f9418577f'
REVIEWED_BASES=frozenset((BASE, REMOTE_BASE))
EXPIRES=1802304000  # 2027-02-11T00:00:00+00:00
OWNER='maintainer'
DELTA={
    'scripts/contracts/sdk_surface.py':(
        'f0510e541edcf6a567801362c91045fc8025b6164ac7bf2487a57e4e45599439',
        'f764d453b9f01c528936b843b73342186219e7b7b2b629a78962062c52120d39'),
    'tools/contracts/surface/dotnet/packages.lock.json':(
        None,'03eeadc5ef377c17f787ab65f41fb4c8a9c936bb7f7f4171111fdeec8a81cb46'),
}

def approved(old,new,base,now=None):
    if base not in REVIEWED_BASES or (time.time() if now is None else now)>=EXPIRES: return False
    delta={p:(old.get(p),new.get(p)) for p in old.keys()|new.keys() if old.get(p)!=new.get(p)}
    return delta==DELTA
