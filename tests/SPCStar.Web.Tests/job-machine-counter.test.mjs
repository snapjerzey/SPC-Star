import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source = fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js', import.meta.url), 'utf8');
test('next inspection uses the server-calculated due counter for that variable, not the last unrelated completion', () => {
  const fast={characteristicName:'Fast',frequencyType:'Quantity',frequencyUnit:'Pieces',frequencyValue:2000};
  const slow={characteristicName:'Slow',frequencyType:'Quantity',frequencyUnit:'Pieces',frequencyValue:4000};
  const c = vm.createContext({state:{selectedPlans:[fast,slow],contexts:[{nextMachineCounterDue:4100},{nextMachineCounterDue:4100}]}});
  for(const name of ['isFiniteValue','planUsesMachineCounterFrequency','nextMachineCounterDueForPlan']) {
    const start=source.indexOf(`function ${name}(`);
    vm.runInContext(source.slice(start,source.indexOf('\nfunction ',start+1)),c);
  }
  assert.equal(c.nextMachineCounterDueForPlan(fast),4100);
  assert.equal(c.nextMachineCounterDueForPlan(slow),4100);
  assert.equal(c.nextMachineCounterDueForPlan({...slow}),4100);
  assert.equal(c.nextMachineCounterDueForPlan({...slow,operationSeq:20}),null);
  c.state.contexts=[{nextMachineCounterDue:6100},{nextMachineCounterDue:8100}];
  assert.equal(c.nextMachineCounterDueForPlan(fast),6100);
  assert.equal(c.nextMachineCounterDueForPlan(slow),8100);
  c.state.contexts=[];assert.equal(c.nextMachineCounterDueForPlan(fast),null);
});
test('job counter includes other machines, phases and failures, but excludes other jobs and drafts', () => {
  const c = vm.createContext({ state: { contexts: [{minimumMachineCounter: 189000}], jobNotes: [
    {jobNum:'JOB', resourceId:'OTHER', inspectionPhase:'Coil Change', entryType:'PhaseComplete', machineCounter:189000},
    {jobNum:'JOB', entryType:'InspectionFailed', machineCounter:189100},
    {jobNum:'OTHER', entryType:'PhaseComplete', machineCounter:999999},
    {jobNum:'JOB', entryType:'Measurement', machineCounter:999999}
  ] }, $: () => ({value:'JOB'}), counter:'138000', machineCounterValue: () => c.counter });
  for (const name of ['minimumJobMachineCounter','machineCounterBelowJobMinimum','machineCounterComplete']) {
    const start=source.indexOf(`function ${name}(`);
    vm.runInContext(source.slice(start,source.indexOf('\nfunction ',start+1)),c);
  }
  assert.equal(c.minimumJobMachineCounter(),189100);
  assert.equal(c.machineCounterComplete(),false);
  c.counter='189100';
  assert.equal(c.machineCounterComplete(),true);
  c.counter='189101';
  assert.equal(c.machineCounterComplete(),true);
});
