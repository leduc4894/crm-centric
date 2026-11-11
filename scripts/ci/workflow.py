"""Offline contract lint for the mandatory Wave 0 workflow."""
import re

JOBS = {'backend', 'compose', 'contracts-sdk', 'web-browser', 'security'}

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
    for name, job in data['jobs'].items():
        if not isinstance(job.get('timeout-minutes'), int) or not 1 <= job['timeout-minutes'] <= 90:
            raise ValueError('CI_WORKFLOW_TIMEOUT_INVALID')
        if name in JOBS and 'if' in job:
            raise ValueError('CI_WORKFLOW_SKIPPED_JOB')
        for step in job['steps']:
            if step.get('continue-on-error'):
                raise ValueError('CI_WORKFLOW_ERROR_IGNORED')
            if 'uses' in step and not re.fullmatch(r'[\w/-]+@[0-9a-f]{40}', step['uses']):
                raise ValueError('CI_ACTION_UNPINNED')
            if step.get('uses', '').startswith('actions/checkout@') and step.get('with', {}).get('persist-credentials') is not False:
                raise ValueError('CI_CHECKOUT_CREDENTIALS')
        if name in JOBS:
            if not any(step.get('if') == 'always()' and step.get('uses','').startswith('actions/upload-artifact@') for step in job['steps']):
                raise ValueError('CI_REPORT_MISSING')

if __name__ == '__main__':
    import yaml
    from pathlib import Path
    validate(yaml.safe_load(Path('.github/workflows/wave0.yml').read_text()))
    print('workflow contract: PASS')
