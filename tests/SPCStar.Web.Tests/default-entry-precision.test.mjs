import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';

const source = fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js', import.meta.url), 'utf8');
const names = ['measurementEntryDecimalPlaces', 'decimalPlacesForNumber', 'requiresFourDecimalMeasurementEntry',
  'scheduleAutoAdvanceMeasurement',
  'requiresThreeDecimalMeasurementEntry', 'isCuttingEdgeNeedleBlankL3', 'isCalculatedRemovalPlan',
  'requiresSingleZeroMeasurementStarter', 'normalizedInspectionItemName', 'inspectionItemStartsWith',
  'measurementInputExpectedDecimalPlaces', 'measurementInputReadyToAutoAdvance',
  'measurementInputHasIncompletePrecision', 'measurementInputHasIncompleteWholeNumber', 'firstFiniteValue',
  'isFiniteValue', 'formatMeasurementNumber', 'formatNumber', 'measurementEntryStarter',
  'suppressesDeviceMeasurementStarter', 'isCuttingEdgeIndicatorStarterPlan', 'isTaperpointIndicatorStarterPlan',
  'measurementEntryNumbers', 'leadingDecimalZeroCount'];
function fixture(plan) {
  const context = vm.createContext({ state: { selectedPlans: [plan], contexts: [] },
    isBoxNumberInspectionPlan: () => false, inputHasValue: input => !!input.value,
    capMeasurementDecimalPlaces: value => value });
  for (const name of names) {
    const start = source.indexOf(`function ${name}(`);
    assert.notEqual(start, -1, name);
    vm.runInContext(source.slice(start, source.indexOf('\nfunction ', start + 1)), context);
  }
  return context;
}

test('auto-advance waits one full idle second and restarts when another digit is entered', async () => {
  for (const addDigit of [false, true]) {
    const context = fixture({ characteristicName: 'Generic measurement', lslText: '0.00', uslText: '0.09' });
    let now = 0, nextId = 0;
    const timers = new Map(), saved = [];
    context.window = {
      clearTimeout: id => timers.delete(id),
      setTimeout: (callback, delay) => {
        assert.equal(delay, 1000);
        timers.set(++nextId, { callback, at: now + delay });
        return nextId;
      }
    };
    context.submitSingleMeasurementAndAdvance = async (input, moveNext) => saved.push([input.value, moveNext, now]);
    const tick = async time => {
      now = time;
      for (const [id, timer] of timers) {
        if (timer.at <= now) { timers.delete(id); await timer.callback(); }
      }
    };
    const input = { value: '.0', dataset: { planIndex: '0', entryType: 'Variable', entryDecimals: '2' } };
    context.scheduleAutoAdvanceMeasurement(input);
    assert.equal(timers.size, 0);
    input.value = '.00';
    context.scheduleAutoAdvanceMeasurement(input);
    await tick(999);
    assert.equal(saved.length, 0);
    if (addDigit) {
      input.value = '.000';
      context.scheduleAutoAdvanceMeasurement(input);
      await tick(1000);
      assert.equal(saved.length, 0);
      await tick(1998);
      assert.equal(saved.length, 0);
      await tick(1999);
      assert.deepEqual(saved, [['.000', true, 1999]]);
    } else {
      await tick(1000);
      assert.deepEqual(saved, [['.00', true, 1000]]);
    }
    assert.equal(timers.size, 0);
    assert.equal(input.dataset.autoSubmitting, 'false');
  }
});

test('editing a ready reading back to an incomplete decimal cancels its pending advance', () => {
  const context = fixture({ characteristicName: 'Generic measurement', lslText: '0.00', uslText: '0.09' });
  const timers = new Map();
  context.window = { clearTimeout: id => timers.delete(id), setTimeout: callback => { timers.set(1, callback); return 1; } };
  const input = { value: '.00', dataset: { planIndex: '0', entryType: 'Variable', entryDecimals: '2' } };
  context.scheduleAutoAdvanceMeasurement(input);
  assert.equal(timers.size, 1);
  input.value = '.0';
  context.scheduleAutoAdvanceMeasurement(input);
  assert.equal(timers.size, 0);
});

test('ordinary decimal fields require spec digits but accept additional digits, including zero and one-sided limits', () => {
  for (const [lslText, uslText, partial, full, precision] of [
    ['0.0', '0.9', '.', '.4', 1],
    ['0.00', '0.09', '.0', '.04', 2],
    [null, '0.010', '.00', '.009', 3],
    ['0.0010', null, '.001', '.0012', 4]
  ]) {
    const plan = { characteristicName: 'Generic measurement', lslText, uslText,
      lsl: lslText === null ? null : Number(lslText), usl: uslText === null ? null : Number(uslText) };
    const context = fixture(plan);
    assert.equal(context.measurementEntryDecimalPlaces(plan), precision);
    const input = { value: partial, dataset: { planIndex: '0', entryType: 'Variable', entryDecimals: '0' } };
    assert.equal(context.measurementInputReadyToAutoAdvance(input), false);
    assert.equal(context.measurementInputHasIncompletePrecision(input), true);
    input.value = full;
    assert.equal(context.measurementInputReadyToAutoAdvance(input), true);
    assert.equal(context.measurementInputHasIncompletePrecision(input), false);
    assert.equal(input.value, full);
    input.value += '0';
    assert.equal(context.measurementInputReadyToAutoAdvance(input), true);
    assert.equal(context.formatMeasurementNumber(plan.usl ?? plan.lsl, plan), uslText ?? lslText);
  }
});

test('explicit requirements take precedence even when saved spec text exists', () => {
  for (const [group, part, name, lslText, uslText, expected] of [
    ['Schneider', '70306', 'Weld Nub Dia. (2X)', '0.0570', '0.0670', 4],
    ['Schneider', '70306', 'Weld Nub Distance, Edge to Edge of Dia.', '0.1190', '0.1290', 4],
    ['Schneider', '70306', 'Coin Flat', '0.020', '0.060', 3],
    ['Schneider', '70306', 'Angle', '7.0', '9.0', 1],
    ['Schneider', '70310', 'Jaw Tab Center', null, '0.010', 3],
    ['Ethicon Cutting Edge - Needles', '61046', 'Polished L3', '0.0100', '0.0200', 4],
    ['Ethicon Cutting Edge - Needles', '61046', '* Removal', '0.0000', '0.0010', 4],
    ['Ethicon Taperpoint - Needles', '61131', 'Needle Cutoff Length', '1.700', '1.720', 3],
    ['Ethicon Taperpoint - Needles', '61131', 'Crimp Tightness', '2', '4', 3],
    ['Ethicon Taperpoint - Needles', '61055', 'Y Dim - Bottom', '0.0200', '0.0300', 4],
    ['Ethicon Taperpoint - Needles', '61055', 'L26 Dim - Top', '0.0200', '0.0300', 4]
  ]) {
    const plan = { productGroup: group, partNum: part, characteristicName: name, lslText, uslText };
    assert.equal(fixture(plan).measurementEntryDecimalPlaces(plan), expected, name);
  }
});

test('device fields remain unseeded and whole-number checks do not require a decimal', () => {
  for (const [productGroup, characteristicName] of [
    ['Ethicon Cutting Edge - Needles', 'A Dim'], ['Ethicon Cutting Edge - Needles', 'Raw *'],
    ['Ethicon Taperpoint - Needles', 'T Dim - Barrel'], ['Ethicon Cutting Edge - Drilled', 'Hole Depth']
  ]) {
    const plan = { productGroup, characteristicName, lslText: '0.024', uslText: '0.026', lsl: .024, usl: .026 };
    assert.equal(fixture(plan).measurementEntryStarter(plan), '');
  }
  const plan = { characteristicName: 'Brazed Contact To Jaw Shear', lslText: '325', lsl: 325 };
  const context = fixture(plan);
  assert.equal(context.measurementEntryDecimalPlaces(plan), 0);
  assert.equal(context.measurementInputReadyToAutoAdvance({ value: '350',
    dataset: { planIndex: '0', entryType: 'Variable', entryDecimals: '0' } }), true);
});
