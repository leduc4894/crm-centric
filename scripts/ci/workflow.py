"""Offline contract lint for the mandatory Wave 0 workflow."""
import re
import json

JOBS = {'backend', 'compose', 'contracts-sdk', 'web-browser', 'security'}
ARTIFACT_ACTION_PINS = {
    'actions/upload-artifact': 'cf430e030ddbb5b0abf93d22962f4752f3646cd9',
    'actions/download-artifact': '9000827ccba6bdab643e8b6fd33ac0654aef8333',
}

def validate(data):
    events = data.get('on', data.get(True))
    if set(events) != {'push', 'pull_request'} or events['push'].get('branches') != ['main']:
        raise ValueError('CI_WORKFLOW_EVENT_INVALID')
    if data.get('permissions') != {'contents':'read'}:
        raise ValueError('CI_WORKFLOW_PERMISSION_INVALID')
    if set(data['jobs']) != JOBS | {'wave0-required'}:
        raise ValueError('CI_WORKFLOW_INVENTORY_INVALID')
    gate = data['jobs']['wave0-required']
    if set(gate.get('needs', [])) != JOBS or gate.get('if') != 'always()':
        raise ValueError('CI_WORKFLOW_AGGREGATE_INVALID')
    if any(key in data for key in ('secrets','env','environment')):
        raise ValueError('CI_WORKFLOW_TRUST_INVALID')
    for name, job in data['jobs'].items():
        if any(key in job for key in ('permissions','secrets','environment','env','container','services','uses')):
            raise ValueError('CI_WORKFLOW_TRUST_INVALID')
        if job.get('runs-on') != 'ubuntu-24.04':
            raise ValueError('CI_WORKFLOW_RUNNER_INVALID')
        if not isinstance(job.get('timeout-minutes'), int) or not 1 <= job['timeout-minutes'] <= 90:
            raise ValueError('CI_WORKFLOW_TIMEOUT_INVALID')
        if name in JOBS and 'if' in job:
            raise ValueError('CI_WORKFLOW_SKIPPED_JOB')
        for step in job['steps']:
            serialized=json.dumps(step)
            expressions=re.findall(r'\$\{\{(.*?)\}\}',serialized)
            allowed_expressions={'github.workspace','toJSON(steps)','toJSON(needs)',
                                 'steps.scanner-python.outputs.python-path'}
            if any(expression.strip() not in allowed_expressions for expression in expressions):
                raise ValueError('CI_WORKFLOW_UNTRUSTED_EXPRESSION')
            action=step.get('uses','').split('@')[0]
            allowed={'actions/checkout','actions/setup-python','actions/setup-node','actions/setup-dotnet',
                     'actions/upload-artifact','actions/download-artifact'}
            if action and action not in allowed:
                raise ValueError('CI_WORKFLOW_ACTION_INVALID')
            if action in ARTIFACT_ACTION_PINS and step.get('uses') != action+'@'+ARTIFACT_ACTION_PINS[action]:
                raise ValueError('CI_ACTION_PIN_INVALID')
            if action=='actions/upload-artifact':
                config=step.get('with',{})
                paths={'.local/ci/'+name+'.json':name+'-summary'}
                if name=='security': paths['.local/ci-sbom/source.json']='source-sbom'
                if (config.get('path') not in paths or config.get('name')!=paths[config['path']]
                        or config.get('retention-days')!=7 or config.get('if-no-files-found')!='error'):
                    raise ValueError('CI_WORKFLOW_ARTIFACT_INVALID')
            if action=='actions/download-artifact':
                if name!='wave0-required' or step.get('with')!={'name':step.get('id','')+'-summary','path':'.local/ci'} or step.get('id') not in JOBS:
                    raise ValueError('CI_WORKFLOW_ARTIFACT_INVALID')
            if step.get('if')=='always()' and 'run' in step:
                command=step['run']
                if not ('scripts.ci.completion' in command or 'CI_TRUST_NOT_PROVISIONED' in command or (name=='wave0-required' and step.get('id')=='aggregate' and 'scripts.ci.aggregate' in command)):
                    raise ValueError('CI_WORKFLOW_ALWAYS_INVALID')
            if step.get('continue-on-error'):
                raise ValueError('CI_WORKFLOW_ERROR_IGNORED')
            if 'uses' in step and not re.fullmatch(r'[\w/-]+@[0-9a-f]{40}', step['uses']):
                raise ValueError('CI_ACTION_UNPINNED')
            if step.get('uses', '').startswith('actions/checkout@') and step.get('with', {}).get('persist-credentials') is not False:
                raise ValueError('CI_CHECKOUT_CREDENTIALS')
        if name in JOBS:
            completions=[step for step in job['steps'] if 'scripts.ci.completion' in step.get('run','')]
            gates=[step for step in job['steps'] if step.get('id')=='gate']
            if (len(completions)!=1 or completions[0].get('if')!='always()'
                    or len(gates)!=1 or 'if' in gates[0]):
                raise ValueError('CI_WORKFLOW_COMPLETION_INVALID')
            if not any(step.get('if') == 'always()' and step.get('uses','').startswith('actions/upload-artifact@') for step in job['steps']):
                raise ValueError('CI_REPORT_MISSING')

if __name__ == '__main__':
    import yaml
    from pathlib import Path
    validate(yaml.safe_load(Path('.github/workflows/wave0.yml').read_text()))
    print('workflow contract: PASS')
