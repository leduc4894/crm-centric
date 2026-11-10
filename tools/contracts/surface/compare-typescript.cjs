'use strict';
const ts = require('typescript');
const { randomUUID } = require('node:crypto');
const [base, candidate] = process.argv.slice(2);
const program = ts.createProgram([base, candidate], { strict: true, noEmit: true, skipLibCheck: false, target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.NodeNext, moduleResolution: ts.ModuleResolutionKind.NodeNext });
if (ts.getPreEmitDiagnostics(program).some(d => d.category === ts.DiagnosticCategory.Error)) {
  process.stdout.write(JSON.stringify({ compatible: false, error: 'SDK_SURFACE_UNAVAILABLE', correlationId: randomUUID() }));
  process.exit(1);
}
const checker = program.getTypeChecker();
function exportsOf(path) {
  const source = program.getSourceFile(path);
  return new Map(checker.getExportsOfModule(checker.getSymbolAtLocation(source)).map(s => [s.name, s]));
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
process.stdout.write(JSON.stringify({ compatible }));
