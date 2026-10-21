import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';

const source = fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js', import.meta.url), 'utf8');
const functions = ['measurementEntryDecimalPlaces', 'measurementEntryNumbers', 'decimalPlacesForNumber',
  'normalizedInspectionItemName', 'measurementInputExpectedDecimalPlaces', 'measurementInputReadyToAutoAdvance',
  'measurementInputHasIncompletePrecision', 'formatMeasurementNumber', 'formatNumber'];

test('70306 readings wait for full precision, preserve zeros, and accept a decimal angle in every phase', () => {
  for (const phase of ['Startup', 'Setup', 'In Process', 'Coil Change']) {
    for (const [name, lsl, usl, partial, full, precision] of [
      ['Weld Nub Dia. (2X)', .057, .067, '.062', '.0620', 4],
      ['Weld Nub Distance, Edge to Edge of Dia.', .119, .129, '.124', '.1240', 4],
      ['Coin Flat', .02, .06, '.04', '.040', 3],
      ['Angle', 7, 9, '7', '7.2', 1]
    ]) {
      const plan = { partNum: '70306', productGroup: 'Schneider', inspectionPhase: phase, characteristicName: name, lsl, usl };
      const context = vm.createContext({
        state: { selectedPlans: [plan], contexts: [] }, requiresFourDecimalMeasurementEntry: () => false,
        requiresThreeDecimalMeasurementEntry: () => false, isBoxNumberInspectionPlan: () => false,
        inputHasValue: input => !!input.value, capMeasurementDecimalPlaces: value => value,
        measurementInputHasIncompleteWholeNumber: () => false, isFiniteValue: value => Number.isFinite(Number(value)),
        formatNumber: value => String(value)
      });
      for (const name of functions) {
        const start = source.indexOf(`function ${name}(`);
        const end = source.indexOf('\nfunction ', start + 1);
        vm.runInContext(source.slice(start, end), context);
      }
      assert.equal(context.measurementEntryDecimalPlaces(plan), precision);
      const input = { value: partial, dataset: { planIndex: '0', entryType: 'Variable', entryDecimals: '0' } };
      assert.equal(context.measurementInputReadyToAutoAdvance(input), false);
      assert.equal(context.measurementInputHasIncompletePrecision(input), true);
      input.value = full;
      assert.equal(context.measurementInputReadyToAutoAdvance(input), true);
      assert.equal(context.measurementInputHasIncompletePrecision(input), false);
      assert.equal(context.formatMeasurementNumber(usl, plan), usl.toFixed(precision));
    }
  }
});

test('ordinary decimal measurements use saved spec digits, ignoring target and control precision', () => {
  const context = vm.createContext({ requiresFourDecimalMeasurementEntry: () => false,
    requiresThreeDecimalMeasurementEntry: () => false, decimalPlacesForNumber: value => String(value).split('.')[1]?.length || 0 });
  const start = source.indexOf('function measurementEntryDecimalPlaces(');
  const end = source.indexOf('\nfunction ', start + 1);
  vm.runInContext(source.slice(start, end), context);
  for (const [lslText, uslText, expected] of [
    ['0.0570', '0.0670', 4], ['0.020', '0.060', 3], ['7.0', '9.0', 1],
    [null, '0.010', 3], ['325', null, 0], ['0.10', '0.2000', 4]
  ]) {
    assert.equal(context.measurementEntryDecimalPlaces({ characteristicName: 'Any Variable', lslText, uslText, nominal: .123456, lcl: .000001 }), expected);
  }
});

test('spec entry precision replaces stale field precision without rounding an exact target', () => {
  const plan = { characteristicName: 'Any Variable', lslText: '0.024', uslText: '0.025', lsl: .024, usl: .025 };
  const context = vm.createContext({ state: { selectedPlans: [plan], contexts: [] },
    requiresFourDecimalMeasurementEntry: () => false, requiresThreeDecimalMeasurementEntry: () => false });
  for (const name of functions) {
    const start = source.indexOf(`function ${name}(`);
    vm.runInContext(source.slice(start, source.indexOf('\nfunction ', start + 1)), context);
  }
  assert.equal(context.measurementInputExpectedDecimalPlaces({ dataset: { planIndex: '0', entryDecimals: '4' } }), 3);
  assert.equal(context.formatMeasurementNumber(.024, plan), '0.024');
  assert.equal(context.formatMeasurementNumber(.0245, plan), '0.0245');
});
