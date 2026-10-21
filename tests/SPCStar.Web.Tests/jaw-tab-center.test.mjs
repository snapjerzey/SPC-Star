import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';

const source = fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js', import.meta.url), 'utf8');
const functions = ['requiresThreeDecimalMeasurementEntry', 'normalizedInspectionItemName', 'inspectionItemStartsWith', 'isGagePinBlockHoleDiameterPlan',
  'measurementEntryDecimalPlaces', 'measurementEntryNumbers', 'decimalPlacesForNumber',
  'measurementInputReadyToAutoAdvance', 'measurementInputHasIncompleteWholeNumber', 'measurementInputWouldLock',
  'measurementInputExpectedDecimalPlaces', 'measurementInputHasIncompletePrecision',
  'firstFiniteValue', 'isFiniteValue', 'formatMeasurementNumber', 'formatNumber'];

test('Jaw Tab Center waits for three decimals and displays the exact upper limit as 0.010', async () => {
  for (const partNum of ['70305', '70307', '70310']) {
    for (const inspectionPhase of ['Setup', 'Startup', 'In Process']) {
      const plan = { partNum, inspectionPhase, productGroup: 'Schneider', characteristicName: 'Jaw Tab Center', lsl: null, usl: 0.010 };
      const context = vm.createContext({
        state: { selectedPlans: [plan], contexts: [{ lowerSpecLimit: null, upperSpecLimit: 0.010 }] },
        requiresFourDecimalMeasurementEntry: () => false,
        isBoxNumberInspectionPlan: () => false,
        inputHasValue: input => Boolean(input.value),
        capMeasurementDecimalPlaces: value => value
      });
      for (const name of functions) {
        const start = source.indexOf(`function ${name}(`);
        const end = source.indexOf('\nfunction ', start + 1);
        vm.runInContext(source.slice(start, end), context);
      }
      const decimals = context.measurementEntryDecimalPlaces(plan);
      assert.equal(decimals, 3);
      assert.equal(context.formatMeasurementNumber(0.010, plan), '0.010');
      const input = { value: '', dataset: { planIndex: '0', entryType: 'Variable', entryDecimals: '2' } };
      for (const value of ['.0', '.00', '.01']) {
        input.value = value;
        assert.equal(context.measurementInputReadyToAutoAdvance(input), false);
        assert.equal(context.measurementInputHasIncompletePrecision(input), true);
      }
      for (const value of ['.001', '.010']) {
        input.value = value;
        assert.equal(context.measurementInputReadyToAutoAdvance(input), true);
        assert.equal(context.measurementInputHasIncompletePrecision(input), false);
        assert.equal(context.measurementInputWouldLock(input, plan, context.state.contexts[0]), false);
      }
      input.value = '.011';
      assert.equal(context.measurementInputWouldLock(input, plan, context.state.contexts[0]), true);
      const listeners = new Map();
      const saved = [];
      input.closest = () => null;
      input.addEventListener = (name, callback) => listeners.set(name, callback);
      context.document = { querySelectorAll: () => [input] };
      context.submitSingleMeasurementAndAdvance = async field => saved.push(field.value);
      vm.runInContext(source.slice(source.indexOf('function wireMeasurementDeviceInputs('), source.indexOf('function updateCurrentPlanStatus(')), context);
      context.wireMeasurementDeviceInputs();
      input.value = '.01';
      await listeners.get('blur')();
      assert.equal(saved.length, 0);
      input.value = '.001';
      await listeners.get('blur')();
      assert.deepEqual(saved, ['.001']);
    }
  }
});
