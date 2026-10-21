import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import test from 'node:test';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
test('Configured needle blank remains visible in every drilling phase',()=>{
 for(const inspectionPhase of ['Setup','In Process']){
  const rows={innerHTML:''};
  const context=vm.createContext({state:{snapshot:{partMaterialFields:[{partNum:'71355',inspectionPhase:'In Process',materialName:'Polished Blank',materialPartNum:'61358',materialDescription:'FS, 29MIL, POLISHED BLANK',isRequired:true,displayOrder:1}],partJobDataFields:[]}},$:()=>rows,MAX_LOT_NUMBER_LENGTH:80,escapeHtml:x=>String(x),currentMaterialLot:()=>null});
  const start=source.indexOf('function renderConfiguredMaterialFields('),end=source.indexOf('\nfunction ',start+1);vm.runInContext(source.slice(start,end),context);context.renderConfiguredMaterialFields({partNum:'71355',inspectionPhase});
  assert.match(rows.innerHTML,/value="61358"/);assert.match(rows.innerHTML,/FS, 29MIL, POLISHED BLANK/);assert.match(rows.innerHTML,/Polished Blank/);
 }
});
