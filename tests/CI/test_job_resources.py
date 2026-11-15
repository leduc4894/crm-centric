"""Destructive cleanup is restricted to independently rechecked ownership labels."""
import json
from pathlib import Path
import unittest
from unittest.mock import patch
import yaml

class ResourceTests(unittest.TestCase):
    def test_every_compose_resource_has_explicit_job_owner_label(self):
        config=yaml.safe_load(Path('compose.yaml').read_text())
        for section in ('services','networks','volumes'):
            for resource in config[section].values():
                self.assertEqual(resource['labels']['io.crm-centric.ci-owner'],'${CI_RESOURCE_OWNER:-local}')

    def test_cleanup_rechecks_owner_and_never_removes_foreign_resource(self):
        from scripts.ci.jobs import cleanup_resources
        calls=[]
        def run(command,**kwargs):
            calls.append(command)
            if 'ls' in command: return 'owned\nforeign'
            if 'inspect' in command:
                ident=command[-1]
                labels={'io.crm-centric.ci-owner':'a'*32 if ident=='owned' else 'other'}
                return json.dumps([{'Id':ident,'Name':ident,'Config':{'Labels':labels},'Labels':labels}])
            return ''
        with patch('scripts.ci.jobs.execute',side_effect=run):cleanup_resources('a'*32)
        removed=[c[-1] for c in calls if 'rm' in c]
        self.assertEqual(removed,['owned']*3)
        for command in calls:
            if 'ls' in command:self.assertIn('label=io.crm-centric.ci-owner='+'a'*32,command)

    def test_resource_scope_cleans_on_cancellation_and_restores_environment(self):
        import os
        from scripts.ci.jobs import job_resources
        with patch.dict(os.environ,{'CI_RESOURCE_OWNER':'outer'}), patch('scripts.ci.jobs.cleanup_resources') as cleanup:
            with self.assertRaises(KeyboardInterrupt):
                with job_resources():
                    owner=os.environ['CI_RESOURCE_OWNER']
                    self.assertNotEqual(owner,'outer')
                    raise KeyboardInterrupt
            cleanup.assert_called_once_with(owner)
            self.assertEqual(os.environ['CI_RESOURCE_OWNER'],'outer')

    def test_cleanup_refuses_local_or_non_job_owner(self):
        from scripts.ci.jobs import cleanup_resources, JobError
        with patch('scripts.ci.jobs.execute') as run:
            with self.assertRaises(JobError):cleanup_resources('local')
            run.assert_not_called()
