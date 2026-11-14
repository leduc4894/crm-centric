"""Resolve the complete installed runtime closure from the verified npm lock."""
import json
from pathlib import Path
import sys
import uuid


def runtime_packages(root):
    root=Path(root).resolve()
    try:
        locked=json.loads((root/'package-lock.json').read_text())['packages']
        def resolve(parent,name):
            directory=parent
            while directory.is_relative_to(root):
                path=directory/'node_modules'/name
                if path.relative_to(root).as_posix() in locked:
                    return path
                if directory==root: break
                directory=directory.parent
            raise ValueError()
        pending=[]
        for sdk in ('crm','operations'):
            dependencies=json.loads((root/'sdks/typescript'/sdk/'package.json').read_text()).get('dependencies',{})
            for name,version in dependencies.items():
                path=resolve(root,name)
                if locked[path.relative_to(root).as_posix()]['version']!=version: raise ValueError()
                pending.append(path)
        seen=set();names={}
        while pending:
            path=pending.pop()
            if path in seen: continue
            if not path.resolve().is_relative_to(root/'node_modules'): raise ValueError()
            entry=locked[path.relative_to(root).as_posix()]
            package=json.loads((path/'package.json').read_text())
            name=package['name']
            if (name!=path.as_posix().rsplit('/node_modules/',1)[-1]
                    or package['version']!=entry['version']
                    or package.get('dependencies',{})!=entry.get('dependencies',{})
                    or any(package.get(key) for key in ('optionalDependencies','peerDependencies','bundledDependencies'))
                    or (name in names and names[name]!=path)):
                raise ValueError()
            names[name]=path;seen.add(path)
            pending.extend(resolve(path,name) for name in entry.get('dependencies',{}))
        if not seen: raise ValueError()
        return sorted(seen)
    except (OSError,ValueError,KeyError,TypeError,AttributeError):
        raise ValueError('SDK_RUNTIME_PACKAGES_INVALID') from None


if __name__=='__main__':
    try:
        if len(sys.argv)!=2: raise ValueError()
        for path in runtime_packages(Path(sys.argv[1])): print(path)
    except ValueError:
        print(json.dumps({'reasonToken':'SDK_RUNTIME_PACKAGES_INVALID',
                          'correlationId':str(uuid.uuid4())}),file=sys.stderr)
        sys.exit(1)
