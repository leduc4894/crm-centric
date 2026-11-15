"""Compatibility probes operate on real public declaration files."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

import prerequisites

ROOT = Path(__file__).resolve().parents[2]

# The surface comparison runs the real TypeScript compiler and the pinned .NET
# image. A checkout without the pinned prerequisites skips this module with the
# reason of the missing prerequisite; it never fails for a missing tool it
# never provisioned.
@unittest.skipUnless(prerequisites.SDK_SURFACE[0], prerequisites.SDK_SURFACE[1])
class TypeScriptSurfaceTests(unittest.TestCase):
    def compare(self, old, new):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory) / 'base.d.ts'
            candidate = Path(directory) / 'candidate.d.ts'
            base.write_text(old)
            candidate.write_text(new)
            proc = subprocess.run(['node', str(ROOT / 'tools/contracts/surface/compare-typescript.cjs'), str(base), str(candidate)], cwd=ROOT, capture_output=True, text=True, check=True)
            return json.loads(proc.stdout)['compatible']

    def test_additive_exports_pass_but_existing_signature_break_fails(self):
        old = 'export interface operations { get: { input: string; output: string | null } }\nexport interface components { Thing: { id: string } }\n'
        additive = 'export interface operations { get: { input: string; output: string | null }; list: { output: string } }\nexport interface components { Thing: { id: string }; Extra: { value: number } }\n'
        renamed = 'export interface operations { fetch: { input: string; output: string | null } }\nexport interface components { Thing: { id: string } }\n'
        null_removed = 'export interface operations { get: { input: string; output: string } }\nexport interface components { Thing: { id: string } }\n'
        self.assertTrue(self.compare(old, additive))
        self.assertFalse(self.compare(old, renamed))
        self.assertFalse(self.compare(old, null_removed))

    def test_existing_model_member_change_fails_but_new_model_passes(self):
        old = 'export interface components { schemas: { Thing: { id: string; name?: string | null } } }'
        additive = 'export interface components { schemas: { Thing: { id: string; name?: string | null }; Extra: { id: number } } }'
        removed = 'export interface components { schemas: { Thing: { name?: string | null } } }'
        changed = 'export interface components { schemas: { Thing: { id: number; name?: string | null } } }'
        self.assertTrue(self.compare(old, additive))
        self.assertFalse(self.compare(old, removed))
        self.assertFalse(self.compare(old, changed))

    def test_new_operation_on_existing_path_is_additive(self):
        old = 'export interface paths { "/health": { get: { output: string }; post?: never } }'
        new = 'export interface paths { "/health": { get: { output: string }; post: { output: number } } }'
        self.assertTrue(self.compare(old, new))

    def test_unreadable_declarations_report_reason_and_correlation(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory) / 'base.d.ts'
            candidate = Path(directory) / 'candidate.d.ts'
            base.write_text('export const value: number = "not a number";\n')
            candidate.write_text('export const value: number = 1;\n')
            proc = subprocess.run(['node', str(ROOT / 'tools/contracts/surface/compare-typescript.cjs'), str(base), str(candidate)], cwd=ROOT, capture_output=True, text=True)
            self.assertNotEqual(proc.returncode, 0)
            report = json.loads(proc.stdout)
            self.assertFalse(report['compatible'])
            self.assertEqual(report['error'], 'SDK_SURFACE_UNAVAILABLE')
            self.assertTrue(report['correlationId'])

    def test_empty_declaration_file_reports_reason_and_correlation(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory) / 'base.d.ts'
            candidate = Path(directory) / 'candidate.d.ts'
            base.write_text('export interface Thing { id: string }\n')
            candidate.write_text('')
            proc = subprocess.run(['node', str(ROOT / 'tools/contracts/surface/compare-typescript.cjs'), str(base), str(candidate)], cwd=ROOT, capture_output=True, text=True)
            self.assertNotEqual(proc.returncode, 0)
            self.assertEqual(proc.stderr, '', 'a closed error path must not print a raw stack trace')
            report = json.loads(proc.stdout)
            self.assertFalse(report['compatible'])
            self.assertEqual(report['error'], 'SDK_SURFACE_UNAVAILABLE')
            self.assertTrue(report['correlationId'])

@unittest.skipUnless(prerequisites.SDK_SURFACE[0], prerequisites.SDK_SURFACE[1])
class DotnetSurfaceTests(unittest.TestCase):
    def test_additive_members_pass_but_removed_or_changed_members_fail(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name, source in {
                'base': 'public class Thing { public string? Name {get;set;} public int Get(int x) => x; }',
                'additive': 'public class Thing { public string? Name {get;set;} public int Get(int x) => x; public void Extra() {} } public class NewThing {}',
                'removed': 'public class Thing { public string? Name {get;set;} }',
                'nullability': 'public class Thing { public string Name {get;set;} = ""; public int Get(int x) => x; }',
            }.items():
                path = root / name
                path.mkdir()
                (path / 'Example.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup></Project>')
                (path / 'Class.cs').write_text(source)
            image = json.loads((ROOT / 'tools/contracts/toolchain.json').read_text())['dotnetImage']
            cmd = ['docker', 'run', '--rm', '--network', 'none', '--pull=never', '-u', f'{os.getuid()}:{os.getgid()}', '-e', 'HOME=/tmp', '-e', 'DOTNET_CLI_HOME=/tmp', '-v', f'{root}:/fixture', '-v', f'{ROOT / "tools/contracts/surface/dotnet"}:/scanner:ro', '-w', '/fixture', image, 'sh', '-ec', 'for x in base additive removed nullability; do dotnet build "$x/Example.csproj" -o "$x/out" >/dev/null; done; cp -r /scanner /fixture/scanner; dotnet build /fixture/scanner/Surface.csproj -o /fixture/scanner/out >/dev/null; for x in additive removed nullability; do dotnet /fixture/scanner/out/Surface.dll /fixture/base/out/Example.dll "/fixture/$x/out/Example.dll"; done; dotnet /fixture/scanner/out/Surface.dll /fixture/missing.dll /fixture/base/out/Example.dll || true']
            proc = subprocess.run(cmd, capture_output=True, text=True, timeout=120, check=True)
            reports = [json.loads(line) for line in proc.stdout.splitlines() if line.startswith('{')]
            results = [report['compatible'] for report in reports[:3]]
            self.assertEqual(results, [True, False, False])
            error = reports[3]
            self.assertFalse(error['compatible'])
            self.assertEqual(error['error'], 'SDK_SURFACE_UNAVAILABLE')
            self.assertTrue(error['correlationId'])
