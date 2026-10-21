import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import test from 'node:test';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
function extract(name){const start=source.indexOf(`function ${name}(`);return source.slice(start,source.indexOf('\nfunction ',start+1));}
test('User Error releases and focuses correction synchronously without restoring the form',()=>{
 const calls=[],deferred=[];
 const input={value:'0.068',dataset:{}};
 const context=vm.createContext({state:{},Date,
  planIndexForLock:()=>2,
  clearActiveLockFromLocalContext:()=>null,
  restoreLockPanelDisplay:()=>calls.push('display'),
  renderLock:()=>calls.push('render'),
  releaseLockMeasurementForCorrection:()=>calls.push('release'),
  focusLockMeasurementInput:()=>calls.push('focus'),
  afterLockScreenPaint:callback=>deferred.push(callback),
  updatePlanStatus:()=>calls.push('status'),
  updateInspectionSubmitState:()=>calls.push('submit'),
  showEntryMessage:()=>{},
  snapshotMeasurementInputs:()=>assert.fail('Must not snapshot the whole form'),
  restoreMeasurementInputSnapshot:()=>assert.fail('Must not overwrite corrections')});
 vm.runInContext(extract('applyLocalLockClear'),context);
 context.applyLocalLockClear({alertId:'one'});
 assert.deepEqual(calls,['display','render','release','focus']);
 input.value='0.066';
 deferred.forEach(callback=>callback());
 assert.equal(input.value,'0.066');
 assert.deepEqual(calls,['display','render','release','focus','status','submit']);
});
test('Correction is released in the clear handler, not a later paint callback',()=>{
 const clear=source.slice(source.indexOf('async function clearLock('),source.indexOf('\nfunction hideLockForClearIntent('));
 assert.ok(clear.indexOf('state.pendingLockClearPromise.catch')<clear.indexOf('applyLocalLockClear(clearedLock)'));
 assert.ok(clear.indexOf('applyLocalLockClear(clearedLock)')<clear.indexOf('afterLockScreenPaint('));
});
test('User Error preserves accepted sibling samples and releases only the failed reading',()=>{
 const make=(value)=>({value,dataset:{submitted:'true',lastSubmittedValue:value},closest:()=>({classList:{remove:()=>{}}})});
 const inputs=[make('0.195'),make('0.295'),make('.195')];
 const drafts=[];
 const c=vm.createContext({state:{selectedPlans:[{}],contexts:[{}]},planIndexForLock:()=>0,
  document:{querySelectorAll:()=>inputs},window:{clearTimeout:()=>{}},
  measurementInputWouldLock:input=>Number(input.value)>.205,
  updateMeasurementDraft:input=>drafts.push(input)});
 vm.runInContext(extract('releaseLockMeasurementForCorrection'),c);
 c.releaseLockMeasurementForCorrection({alertId:'one'});
 assert.equal(inputs[0].dataset.submitted,'true');
 assert.equal(inputs[2].dataset.submitted,'true');
 assert.equal(inputs[1].dataset.submitted,'false');
 assert.deepEqual(drafts,[inputs[1]]);
});
test('Blur does not skip unsaved readings just because their text matches an earlier save',()=>{
 const input={value:'.195',disabled:false,dataset:{submitted:'false',lastSubmittedValue:'.195'},listeners:{},addEventListener(name,fn){this.listeners[name]=fn;},closest:()=>({classList:{remove(){}}})};
 let saves=0;
 const c=vm.createContext({document:{querySelectorAll:()=>[input]},inputHasValue:()=>true,measurementInputHasIncompletePrecision:()=>false,submitSingleMeasurementAndAdvance:async()=>{saves++;}});
 vm.runInContext(extract('wireMeasurementDeviceInputs'),c);
 c.wireMeasurementDeviceInputs();
 input.listeners.blur();
 assert.equal(saves,1);
 input.dataset.submitted='true';
 input.listeners.blur();
 assert.equal(saves,1);
});
