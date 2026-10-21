import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import test from 'node:test';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
function context(fields){
 const c=vm.createContext({state:{snapshot:{partMaterialFields:fields}}});
 for(const name of ['latestMaterialIssue','requiredMaterialIssueGate']){
  const start=source.indexOf(`function ${name}(`),end=source.indexOf('\nfunction ',start+1);vm.runInContext(source.slice(start,end),c);
 }
 return c;
}
const field={partNum:'61033',materialName:'Steel',materialPartNum:'51495',isRequired:true,inspectionPhase:'In Process'};
const issue={entryType:'Material',jobNum:'61033',partNum:'61033',materialPartNum:'51495',newLotNum:'1',timestamp:'2026-10-16T15:00:00Z',resourceId:'FIRST-MACHINE'};
test('First inspection requires a saved issue, not a typed lot or another job material',()=>{
 const c=context([field]);const set={partNum:'61033',inspectionPhase:'Setup'};
 assert.equal(c.requiredMaterialIssueGate([],'61033',set).allowed,false);
 assert.match(c.requiredMaterialIssueGate([],'61033',set).message,/Steel \(51495\)/);
 assert.equal(c.requiredMaterialIssueGate([{...issue,jobNum:'OTHER'}],'61033',set).allowed,false);
 assert.equal(c.requiredMaterialIssueGate([issue],'61033',set).allowed,true);
});

test('Four contact tape fields keep independent lots despite sharing a material part number',()=>{
 const names=['Contact Tape A - BOA Lot','Contact Tape A - Supplier Lot','Contact Tape B - BOA Lot','Contact Tape B - Supplier Lot'];
 const fields=names.map(materialName=>({...field,partNum:'70122',materialPartNum:'50041',materialName}));
 const c=context(fields);
 const history=names.map((materialName,i)=>({...issue,jobNum:'70122',partNum:'70122',materialPartNum:'50041',materialName,newLotNum:`LOT-${i}`}));
 assert.equal(c.requiredMaterialIssueGate(history.slice(0,1),'70122',{partNum:'70122'}).allowed,false);
 assert.equal(c.requiredMaterialIssueGate(history,'70122',{partNum:'70122'}).allowed,true);
 names.forEach((name,i)=>assert.equal(c.latestMaterialIssue(history,'70122','70122','50041',name).newLotNum,`LOT-${i}`));
});
test('Latest saved replacement is current across phases and machines; old issue is retained',()=>{
 const c=context([field]),replacement={...issue,newLotNum:'2',timestamp:'2026-10-17T15:00:00Z',resourceId:'SECOND-MACHINE'};const history=[issue,replacement];
 for(const inspectionPhase of ['Setup','Startup','In Process','Coil Change','Spool','End of Spool'])assert.equal(c.requiredMaterialIssueGate(history,'61033',{partNum:'61033',inspectionPhase}).allowed,true);
 assert.equal(c.latestMaterialIssue(history,'61033','61033','51495').newLotNum,'2');assert.equal(history.length,2);
});
test('Every required material must be issued; optional materials do not block',()=>{
 const c=context([field,{...field,materialName:'Second',materialPartNum:'SECOND'},{...field,materialPartNum:'OPTIONAL',isRequired:false}]);
 assert.equal(c.requiredMaterialIssueGate([issue],'61033',{partNum:'61033'}).allowed,false);
 assert.equal(c.requiredMaterialIssueGate([issue,{...issue,materialPartNum:'SECOND'}],'61033',{partNum:'61033'}).allowed,true);
});
test('Saved lots are shown beside the material and replacement input is optional until changed',()=>{
 const rows={innerHTML:''};const c=vm.createContext({state:{snapshot:{partMaterialFields:[field],partJobDataFields:[]}},$:()=>rows,currentMaterialLot:()=>issue,escapeHtml:x=>String(x),MAX_LOT_NUMBER_LENGTH:20});
 const start=source.indexOf('function renderConfiguredMaterialFields('),end=source.indexOf('\nfunction ',start+1);vm.runInContext(source.slice(start,end),c);c.renderConfiguredMaterialFields({partNum:'61033',inspectionPhase:'Setup'});
 assert.match(rows.innerHTML,/Current lot: <strong>1<\/strong>/);
 assert.match(rows.innerHTML,/<div class="material-field-heading">\s*<h3>[\s\S]*?<\/h3>\s*<p class="current-material-lot"[^>]*>Current lot: <strong>1<\/strong><\/p>\s*<\/div>\s*<label>/);
 assert.doesNotMatch(rows.innerHTML,/<input[^>]*class="material-lot-input"[^>]*required/);
 assert.match(rows.innerHTML,/<option value="Material change" selected>/);
});
