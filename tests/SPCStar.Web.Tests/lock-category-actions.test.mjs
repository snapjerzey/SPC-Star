import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';

const source = fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js', import.meta.url), 'utf8');
const actionFunction = source.slice(source.indexOf('function updateLockCategoryActions('), source.indexOf('function renderVariables('));

test('only User Error exposes Clear Lock; real causes expose End Failed Inspection', () => {
  for (const category of ['Machine', 'Tooling', 'Material', 'User Error']) {
    const nodes = new Map();
    const get = id => {
      if (!nodes.has(id)) {
        const hiddenClasses = new Set();
        const classList = { toggle(name, hidden) { hidden ? hiddenClasses.add(name) : hiddenClasses.delete(name); }, remove(name) { hiddenClasses.delete(name); } };
        nodes.set(id, { value: category, classList, hiddenClasses, closest() { return { classList }; } });
      }
      return nodes.get(id);
    };
    const context = vm.createContext({ $: get, state: {user: {userName: 'operator1'}}, hasPermission: () => false });
    vm.runInContext(actionFunction, context);
    context.updateLockCategoryActions();
    assert.equal(get('clearLockButton').hiddenClasses.has('hidden'), category !== 'User Error');
    assert.equal(get('failInspectionButton').hiddenClasses.has('hidden'), category === 'User Error');
    assert.equal(get('causeTextLabel').hiddenClasses.has('hidden'), category === 'User Error');
    assert.equal(get('solutionTextLabel').hiddenClasses.has('hidden'), category === 'User Error');
    assert.equal(get('overrideUserName').value, category === 'User Error' ? 'operator1' : '');
    assert.equal(get('lockSignoffUserLabel').textContent, category === 'User Error' ? 'User' : 'Line Tech / QA signoff');
  }
});

test('lockout cause selector contains exactly the four permitted choices', () => {
  const html = fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/index.html', import.meta.url), 'utf8');
  const select = html.match(/<select id="causeCategory"[\s\S]*?<\/select>/)[0];
  assert.deepEqual([...select.matchAll(/<option value="([^"]+)"/g)].map(match => match[1]), ['Machine', 'Tooling', 'Material', 'User Error']);
  assert.equal(html.includes('id="bypassReason"'), false);
});
