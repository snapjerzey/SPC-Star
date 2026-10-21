import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
function fixture() {
  const nodes=new Map();
  const get=id=>{if(!nodes.has(id))nodes.set(id,{value:''});return nodes.get(id);};
  const c=vm.createContext({$:get,state:{historyFilters:{}},partNumForJob:()=>'',dateTimeLocalValue:id=>get(id).value||null,URLSearchParams});
  for(const name of ['dataFilterControls','syncHistoryFiltersFrom','dataReviewRequest','ledgerReviewQuery']) {
    const start=source.indexOf(`function ${name}(`);
    vm.runInContext(source.slice(start,source.indexOf('\nfunction ',start+1)),c);
  }
  return {c,get};
}
test('all four data views send the same nine filter dimensions',()=>{
  const {c,get}=fixture();
  for(const [view,ids] of Object.entries(c.dataFilterControls())) {
    assert.equal(ids.length,9);
    ['70306','JOB1','Schneider|10','Coil Change','4S-1','1st Half Days','Angle','2026-10-01T08:00','2026-10-19T16:00'].forEach((v,i)=>get(ids[i]).value=v);
    const query=c.dataReviewRequest(view);
    assert.equal(query.partNum,'70306');assert.equal(query.jobNum,'JOB1');
    assert.equal(query.processCode,'Schneider');assert.equal(query.operationSeq,10);
    assert.equal(query.inspectionPhase,'Coil Change');assert.equal(query.resourceId,'4S-1');
    assert.equal(query.operatorShift,'1st Half Days');assert.equal(query.characteristicName,'Angle');
    assert.equal(query.from,'2026-10-01T08:00');assert.equal(query.to,'2026-10-19T16:00');
    c.syncHistoryFiltersFrom(view);assert.equal(c.state.historyFilters.variable,'Angle');assert.equal(c.state.historyFilters.operation,'Schneider|10');
  }
  const query=new URLSearchParams(c.ledgerReviewQuery());assert.equal(query.get('shift'),'1st Half Days');assert.equal(query.get('resourceId'),'4S-1');
});
test('All selections omit filters rather than retaining a previous scope',()=>{
  const {c}=fixture();
  for(const view of Object.keys(c.dataFilterControls())) {
    const query=c.dataReviewRequest(view);
    assert(Object.values(query).every(value=>value===null));
    c.syncHistoryFiltersFrom(view);assert(Object.values(c.state.historyFilters).every(value=>value===''));
  }
});
