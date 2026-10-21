import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';

const source = fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js', import.meta.url), 'utf8');
const functions = source.slice(source.indexOf('function scheduleAutoAdvanceMeasurement('), source.indexOf('function inspectionEntryComplete('));

function fixture(plan = { characteristicName: 'Brazed Contact To Jaw Shear Force', lsl: 325, usl: null }) {
  const timers = new Map();
  const submissions = [];
  let timerId = 0;
  const context = vm.createContext({
    state: {
      selectedPlans: [plan],
      contexts: [{ lowerSpecLimit: plan.lsl, upperSpecLimit: plan.usl }]
    },
    window: {
      clearTimeout: id => timers.delete(id),
      setTimeout: (callback, delay) => {
        assert.equal(delay, 1000);
        timers.set(++timerId, callback);
        return timerId;
      }
    },
    firstFiniteValue: (...values) => {
      const value = values.find(item => item !== null && item !== undefined && item !== '' && Number.isFinite(Number(item)));
      return value === undefined ? null : Number(value);
    },
    isBoxNumberInspectionPlan: () => false,
    measurementEntryDecimalPlaces: () => 0,
    inputHasValue: input => Boolean(input.value),
    capMeasurementDecimalPlaces: value => value,
    submitSingleMeasurementAndAdvance: async (input, moveNext) => submissions.push({ value: input.value, moveNext })
  });
  vm.runInContext(functions, context);
  const input = { value: '', dataset: { planIndex: '0', entryType: 'Variable', entryDecimals: '0' } };
  return { context, timers, submissions, input };
}

test('pauses after 3 and 35 cannot submit a partial shear reading', async () => {
  const { context, timers, submissions, input } = fixture();
  for (const value of ['3', '35']) {
    input.value = value;
    context.scheduleAutoAdvanceMeasurement(input);
    assert.equal(timers.size, 0);
    assert.equal(submissions.length, 0);
  }
  input.value = '350';
  context.scheduleAutoAdvanceMeasurement(input);
  assert.equal(timers.size, 1);
  await [...timers.values()][0]();
  assert.deepEqual(submissions, [{ value: '350', moveNext: true }]);
});

test('full readings auto-advance without truncating larger values or suppressing low readings', async () => {
  for (const value of ['324', '325', '326', '400', '10000']) {
    const { context, timers, submissions, input } = fixture();
    for (let length = 1; length <= value.length; length++) {
      input.value = value.slice(0, length);
      context.scheduleAutoAdvanceMeasurement(input);
    }
    assert.equal(timers.size, 1);
    await [...timers.values()][0]();
    assert.deepEqual(submissions, [{ value, moveNext: true }]);
  }
});

test('angle does not submit 9 while the operator is entering 90', async () => {
  const { context, timers, submissions, input } = fixture({ characteristicName: 'Jaw With Contact Angle', lsl: 88, usl: 92 });
  input.value = '9';
  context.scheduleAutoAdvanceMeasurement(input);
  assert.equal(timers.size, 0);
  input.value = '90';
  context.scheduleAutoAdvanceMeasurement(input);
  assert.equal(timers.size, 1);
  await [...timers.values()][0]();
  assert.deepEqual(submissions, [{ value: '90', moveNext: true }]);
});

test('losing focus cannot save the partial 9 but can save the complete 90', async () => {
  const { context, submissions, input } = fixture({ characteristicName: 'Jaw With Contact Angle', lsl: 88, usl: 92 });
  const listeners = new Map();
  input.closest = () => null;
  input.addEventListener = (name, callback) => listeners.set(name, callback);
  context.document = { querySelectorAll: () => [input] };
  vm.runInContext(source.slice(source.indexOf('function wireMeasurementDeviceInputs('), source.indexOf('function updateCurrentPlanStatus(')), context);
  context.wireMeasurementDeviceInputs();
  input.value = '9';
  await listeners.get('blur')();
  assert.equal(submissions.length, 0);
  input.value = '90';
  await listeners.get('blur')();
  assert.deepEqual(submissions, [{ value: '90', moveNext: false }]);
});
