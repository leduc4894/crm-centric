"""SDK-free public local stack commands. Raw subprocess diagnostics stay private."""
import json
import sys
import uuid
from local.compose import LocalFailure, LocalStackConfig, run_local_stack
from local.secrets import SecretStateMismatch

if __name__ == '__main__':
    try:
        if len(sys.argv) != 2:
            raise LocalFailure('LOCAL_CONFIGURATION_INVALID')
        sys.exit(run_local_stack(sys.argv[1], LocalStackConfig.from_environment()))
    except (LocalFailure, SecretStateMismatch) as error:
        token = error.token if isinstance(error, LocalFailure) else 'LOCAL_SECRET_STATE_MISMATCH'
    except (OSError, ValueError, KeyError, TypeError):
        token = 'LOCAL_CONFIGURATION_INVALID'
    print(json.dumps({'reasonToken': token, 'service': 'local-stack', 'eventName': 'lifecycle_failed', 'correlationId': uuid.uuid4().hex}), file=sys.stderr)
    sys.exit(1)
