import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
const context=vm.createContext({state:{selectedPlans:[]},firstFiniteValue:(...values)=>values.find(v=>v!==null&&v!==undefined)??null});
for(const name of ['normalizedInspectionItemName','isGagePinBlockHoleDiameterPlan','gagePinDiameterForPlan','applyGagePinInputMapping','measurementInputWouldLock']){
 const start=source.indexOf(`function ${name}(`),end=source.indexOf('\nfunction ',start+1);vm.runInContext(source.slice(start,end),context);
}
const plan={productGroup:'Ethicon Cutting Edge - Drilled',characteristicName:'Hole Dia',inspectionMethod:'Gage Pin Block',lsl:.0162,usl:.0168,gagePinDiameters:['0.0162','0.0163','0.0164','0.0165','0.0166','0.0167','0.0168']};
context.state.selectedPlans=[plan];
test('Mapping consumes calibrated metadata; it never calculates unknown block sizes',()=>{
 assert.equal(context.gagePinDiameterForPlan(plan,7),'0.0168');assert.equal(context.gagePinDiameterForPlan({},1),'');assert.equal(context.gagePinDiameterForPlan(plan,8),'');
});
test('Pin provenance survives normalization; changing a reading clears stale provenance',()=>{
 const input={value:'7',dataset:{planIndex:'0'}};assert.equal(context.applyGagePinInputMapping(input),true);assert.equal(input.value,'0.0168');assert.equal(input.dataset.gagePinNumber,'7');
 context.applyGagePinInputMapping(input);assert.equal(input.dataset.gagePinNumber,'7');input.value='0.0165';context.applyGagePinInputMapping(input);assert.equal(input.dataset.gagePinNumber,'');
});
test('Pin 7 requests immediate lock check even at the source upper limit',()=>{
 assert.equal(context.measurementInputWouldLock({value:'0.0168',dataset:{entryType:'Variable',gagePinNumber:'7'}},plan,{}),true);
 assert.equal(context.measurementInputWouldLock({value:'0.0167',dataset:{entryType:'Variable',gagePinNumber:'6'}},plan,{}),false);
});
