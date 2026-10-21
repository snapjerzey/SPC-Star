import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import test from 'node:test';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
for(const blocked of [false,true])test(blocked?'Saving first issue opens the inspection':'Saving replacement updates current lot without reloading inspection',async()=>{
 const lot={value:'2'},message={},calls=[];let reloads=0,updates=0;
 const row={dataset:{materialName:'Steel',materialDescription:'STEEL'},querySelector:selector=>selector==='.material-part-input'?{value:'51495'}:selector==='.material-lot-input'?lot:{value:blocked?'Material issue at job start':'Material change'}};
 const c=vm.createContext({state:{user:{userName:'operator'},materialIssueBlocked:blocked},selectedValues:()=>({jobNum:'61033',resourceId:'4S-1',set:{partNum:'61033'}}),document:{querySelectorAll:selector=>selector==='.material-field-row'?[row]:[lot]},$:()=>message,MAX_LOT_NUMBER_LENGTH:20,api:async(path,options)=>calls.push({path,data:JSON.parse(options.body)}),newClientRecordId:()=> 'test-material',currentMaterialLot:()=>blocked?null:{newLotNum:'1'},loadJobNotes:async()=>{},updateCurrentMaterialLots:()=>updates++,loadContext:async()=>reloads++,readableError:error=>error.message});
 const start=source.indexOf('async function saveMaterialChange('),end=source.indexOf('\nasync function loadJobNotes(',start+1);vm.runInContext(source.slice(start,end),c);
 await c.saveMaterialChange({preventDefault(){}});
 assert.equal(calls[0].data.materialName,'Steel');assert.equal(calls[0].data.materialDescription,'STEEL');
 assert.equal(calls.length,1);assert.equal(calls[0].data.newLotNum,'2');assert.equal(calls[0].data.oldLotNum,blocked?'':'1');assert.equal(calls[0].data.jobNum,'61033');assert.equal(lot.value,'');assert.equal(updates,1);assert.equal(reloads,blocked?1:0);assert.match(message.textContent,/saved/);
});
