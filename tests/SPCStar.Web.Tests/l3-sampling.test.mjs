import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';

const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
const context=vm.createContext({});
for(const name of ['normalizedInspectionItemName','isCuttingEdgeNeedleBlankL3','measurementSampleLabel']){
  const start=source.indexOf(`function ${name}(`);
  const end=source.indexOf('\nfunction ',start+1);
  vm.runInContext(source.slice(start,end),context);
}

test('Cutting Edge L3 has three Setup pairs but one End of Spool pair',()=>{
  for(const partNum of ['61046','61047','61048','61049','61050','61051','61052','61053','61358','61360','61366','61368','61370','61372','61374']){
    for(const inspectionPhase of ['Setup','End of Spool']){
      const sampleSize=inspectionPhase==='Setup'?6:2;
      const plan={partNum,inspectionPhase,processCode:'Test Polish',productGroup:'Ethicon Cutting Edge - Needles',characteristicName:'Polished L3',sampleSize};
      assert.deepEqual(Array.from({length:sampleSize},(_,i)=>context.measurementSampleLabel(plan,i)),inspectionPhase==='Setup'?[
        'Sample 1 - Left','Sample 1 - Right','Sample 2 - Left','Sample 2 - Right','Sample 3 - Left','Sample 3 - Right'
      ]:['Left','Right']);
    }
  }
});

test('L3 labels do not change L4, drilled L30, legacy pairs or Schneider nubs',()=>{
  const plan={productGroup:'Ethicon Cutting Edge - Needles',characteristicName:'Polished L3',sampleSize:2};
  assert.equal(context.measurementSampleLabel(plan,0),'Left');
  assert.equal(context.measurementSampleLabel(plan,1),'Right');
  assert.equal(context.measurementSampleLabel({...plan,characteristicName:'Polished L4',sampleSize:3},0),'Sample 1');
  assert.equal(context.measurementSampleLabel({...plan,productGroup:'Ethicon Cutting Edge - Drilled',characteristicName:'L30 - Spindle 1'},0),'Sample 1');
  assert.equal(context.measurementSampleLabel({partNum:'70306',characteristicName:'Weld Nub Dia. (2X)',sampleSize:6},5),'Sample 3 - Weld nub 2');
});
