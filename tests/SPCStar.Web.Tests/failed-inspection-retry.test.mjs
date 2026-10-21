import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source = fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js', import.meta.url), 'utf8');
function setup() {
  const plans = [{id: 'first', frequencyType: 'Quantity', frequencyUnit: 'Pieces', frequencyValue: 1500},
    {id: 'later', frequencyType: 'Quantity', frequencyUnit: 'Pieces', frequencyValue: 5000}];
  const message = {classList: {remove(){}}, textContent: ''};
  const c = vm.createContext({state: {selectedPlans: plans, contexts: [{frequencyStatus: {requiresReinspection: true}}, {frequencyStatus: {requiresReinspection: false}}]},
    counter: '3000', machineCounterValue: () => c.counter, machineCounterComplete: () => c.counter !== '',
    nextMachineCounterDueForPlan: () => 4500, selectedInspectionSet: () => ({plans}),
    nextMachineCounterDue: () => 193500,
    selectedSetNeedsMachineCounter: () => true, $: () => message, planIsRequiredForCompletion: () => false,
    machineCounterBelowJobMinimum: () => false,
    minimumJobMachineCounter: () => 189000,
    formatInteger: value => Number(value).toLocaleString('en-US')});
  for (const name of ['planUsesMachineCounterFrequency', 'planRequiresReinspection', 'planIsDueAtCurrentMachineCounter', 'dueStatusForPlan', 'updateMachineCounterDueMessage']) {
    const start = source.indexOf(`function ${name}(`);
    vm.runInContext(source.slice(start, source.indexOf('\nfunction ', start + 1)), c);
  }
  return {c, plans, message};
}
test('failed due checks allow same-counter entry without enabling unrelated frequencies', () => {
  const {c, plans} = setup();
  assert.equal(c.planIsDueAtCurrentMachineCounter(plans[0]), true);
  assert.equal(c.planIsDueAtCurrentMachineCounter({...plans[0]}), true);
  assert.equal(c.planIsDueAtCurrentMachineCounter(plans[1]), false);
  c.counter = '';
  assert.equal(c.planIsDueAtCurrentMachineCounter(plans[0]), false);
});

test('badge uses the next scheduled count, not the first interval', () => {
  const {c, plans} = setup();
  c.state.contexts[0].frequencyStatus.requiresReinspection = false;
  c.nextMachineCounterDueForPlan = () => 139500;
  c.counter = '138000';
  assert.equal(c.planIsDueAtCurrentMachineCounter(plans[0]), false);
  assert.equal(c.dueStatusForPlan(plans[0]).label, 'Not due yet');
  assert.match(c.dueStatusForPlan(plans[0]).title, /139,500/);
  c.counter = '139500';
  assert.equal(c.dueStatusForPlan(plans[0]).label, 'Due now');
  c.state.contexts[0].frequencyStatus.requiresReinspection = true;
  c.counter = '138000';
  assert.equal(c.dueStatusForPlan(plans[0]).label, 'Repeat now');
  assert.equal(c.planIsDueAtCurrentMachineCounter(plans[0]), true);
});
test('retry message is next to the counter and normal schedule resumes after passing', () => {
  const {c, plans, message} = setup();
  c.updateMachineCounterDueMessage();
  assert.match(message.textContent, /Repeat the failed inspection now/);
  c.state.contexts[0].frequencyStatus.requiresReinspection = false;
  assert.equal(c.planIsDueAtCurrentMachineCounter(plans[0]), false);
});

test('blank counter prompts for current job count instead of an obsolete due threshold', () => {
  const {c, message} = setup();
  c.state.contexts[0].frequencyStatus.requiresReinspection = false;
  c.counter = '';
  c.updateMachineCounterDueMessage();
  assert.equal(message.textContent, 'Next inspection due at Machine Counter 193,500. Enter the current Machine Counter.');
  assert.doesNotMatch(message.textContent, /139,500|No inspection due/);
  c.state.contexts[0].frequencyStatus.requiresReinspection = true;
  c.updateMachineCounterDueMessage();
  assert.match(message.textContent, /Repeat the failed inspection now/);
});

test('193500 failure shows immediate repeat until a passing repeat advances next due to 198000', () => {
  const {c, plans, message} = setup();
  plans[0].frequencyValue = 4500;
  plans.splice(1);
  c.minimumJobMachineCounter = () => 193500;
  c.machineCounterBelowJobMinimum = () => c.counter !== '' && Number(c.counter) < 193500;
  let baseline = 189000;
  c.nextMachineCounterDueForPlan = () => baseline + 4500;
  c.nextMachineCounterDue = () => baseline + 4500;
  c.counter = '';
  c.updateMachineCounterDueMessage();
  assert.match(message.textContent, /Repeat the failed inspection now/);
  assert.doesNotMatch(message.textContent, /198,000|No inspection due/);
  c.counter = '193500';
  assert.equal(c.planIsDueAtCurrentMachineCounter(plans[0]), true);
  assert.equal(c.dueStatusForPlan(plans[0]).label, 'Repeat now');
  c.counter = '193499';
  assert.equal(c.planIsDueAtCurrentMachineCounter(plans[0]), false);
  c.counter = '193500';
  c.state.contexts[0].frequencyStatus.requiresReinspection = false;
  baseline = 193500;
  c.counter = '';
  c.updateMachineCounterDueMessage();
  assert.match(message.textContent, /198,000/);
  c.counter = '193500';
  assert.equal(c.planIsDueAtCurrentMachineCounter(plans[0]), false);
});
