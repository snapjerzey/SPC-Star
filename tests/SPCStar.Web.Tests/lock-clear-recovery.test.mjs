import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';

const source = fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js', import.meta.url), 'utf8');
const functions = source.slice(source.indexOf('function finishClearLock('), source.indexOf('function afterLockScreenPaint('));

test('a rejected clear restores the visible lock and rejects pending correction saves', async () => {
  const lock = { alertId: 'lock-1', characteristicName: 'Jaw Width' };
  const nodes = new Map();
  let resolved = false;
  let rejected = false;
  let rendered = null;
  let displayRestored = false;
  const context = vm.createContext({
    state: { activeLock: null, suppressedClearedLockIds: new Set(['lock-1']), pendingLockClearAlertId: 'lock-1', pendingLockClearPromise: {}, pendingClearLock: lock },
    $: id => {
      if (!nodes.has(id)) nodes.set(id, { value: id === 'causeCategory' ? 'User Error' : 'test', textContent: '' });
      return nodes.get(id);
    },
    api: async () => { throw new Error('Invalid override credentials.'); },
    readableError: error => error.message,
    restoreLockPanelDisplay: () => { displayRestored = true; },
    renderLock: value => { rendered = value; },
    showEntryMessage: () => {}
  });
  vm.runInContext(functions, context);
  context.finishClearLock(lock, () => { resolved = true; }, () => { rejected = true; });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(resolved, false);
  assert.equal(rejected, true);
  assert.equal(displayRestored, true);
  assert.equal(rendered, lock);
  assert.equal(context.state.activeLock, lock);
  assert.equal(context.state.suppressedClearedLockIds.has('lock-1'), false);
  assert.match(nodes.get('overrideMessage').textContent, /Invalid override credentials/);
});

test('a successful clear remains hidden and resolves pending correction saves', async () => {
  const lock = { alertId: 'lock-1' };
  const nodes = new Map();
  let resolved = false;
  let rendered = false;
  const context = vm.createContext({
    state: { activeLock: null, suppressedClearedLockIds: new Set(['lock-1']), pendingLockClearAlertId: 'lock-1', pendingLockClearPromise: {}, pendingClearLock: lock },
    $: id => { if (!nodes.has(id)) nodes.set(id, { value: 'User Error' }); return nodes.get(id); },
    api: async () => ({}),
    selectedValues: () => ({ jobNum: '' }),
    renderLock: () => { rendered = true; }
  });
  vm.runInContext(functions, context);
  context.finishClearLock(lock, () => { resolved = true; }, () => assert.fail('Successful clear must not reject'));
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(resolved, true);
  assert.equal(rendered, false);
  assert.equal(context.state.pendingLockClearPromise, null);
  assert.equal(context.state.activeLock, null);
});
