'use strict';
const { randomUUID } = require('node:crypto');

// A declaration file without a module symbol exposes no public surface at all.
class NoDeclarations extends Error {}

function envelope(cause) {
  return JSON.stringify({ compatible: false, error: 'SDK_SURFACE_UNAVAILABLE', cause, correlationId: randomUUID() });
}

function compare(ts, base, candidate) {
  const program = ts.createProgram([base, candidate], { strict: true, noEmit: true, skipLibCheck: false, target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.NodeNext, moduleResolution: ts.ModuleResolutionKind.NodeNext });
  if (ts.getPreEmitDiagnostics(program).some(d => d.category === ts.DiagnosticCategory.Error)) {
    return { exit: 1, output: envelope('unreadable-declarations') };
  }
  const checker = program.getTypeChecker();
  function exportsOf(path) {
    const source = program.getSourceFile(path);
    const module = source && checker.getSymbolAtLocation(source);
    if (!module) throw new NoDeclarations();
    return new Map(checker.getExportsOfModule(module).map(s => [s.name, s]));
  }
  function symbolType(symbol) {
    return (symbol.flags & ts.SymbolFlags.Type) ? checker.getDeclaredTypeOfSymbol(symbol) : checker.getTypeOfSymbolAtLocation(symbol, symbol.valueDeclaration || symbol.declarations[0]);
  }
  function members(type) { return new Map(checker.getPropertiesOfType(type).map(s => [s.name, s])); }
  function same(a, b) {
    return checker.isTypeAssignableTo(a, b) && checker.isTypeAssignableTo(b, a);
  }
  function compareMembers(oldType, newType, level, paths) {
    const oldMembers = members(oldType), newMembers = members(newType);
    for (const [name, previous] of oldMembers) {
      if (paths && level === 0 && (previous.flags & ts.SymbolFlags.Optional) && (checker.getNonNullableType(symbolType(previous)).flags & ts.TypeFlags.Never)) continue;
      const next = newMembers.get(name);
      if (!next) return false;
      const oldMember = symbolType(previous), newMember = symbolType(next);
      if (level > 0) {
        if (!compareMembers(oldMember, newMember, level - 1, paths)) return false;
      } else if (!same(oldMember, newMember) || !!(previous.flags & ts.SymbolFlags.Optional) !== !!(next.flags & ts.SymbolFlags.Optional)) return false;
    }
    return true;
  }
  const oldExports = exportsOf(base), newExports = exportsOf(candidate);
  let compatible = true;
  for (const [name, previous] of oldExports) {
    const next = newExports.get(name);
    if (!next) { compatible = false; break; }
    const oldType = symbolType(previous), newType = symbolType(next);
    const depth = name === 'components' || name === 'paths' ? 2 : name === 'operations' ? 1 : 0;
    if (depth ? !compareMembers(oldType, newType, depth - 1, name === 'paths') : !same(oldType, newType)) { compatible = false; break; }
  }
  return { exit: 0, output: JSON.stringify({ compatible }) };
}

try {
  // The compiler is loaded inside the guarded region: a checkout without a
  // resolvable typescript module is a closed error path with a reason token
  // and a correlation identifier, never a raw Node stack trace.
  const ts = require('typescript');
  const result = compare(ts, process.argv[2], process.argv[3]);
  process.stdout.write(result.output);
  process.exitCode = result.exit;
} catch (error) {
  const cause = error && error.code === 'MODULE_NOT_FOUND'
    ? 'typescript-unavailable'
    : error instanceof NoDeclarations ? 'no-declarations' : 'comparison-failed';
  process.stdout.write(envelope(cause));
  process.exitCode = 1;
}
