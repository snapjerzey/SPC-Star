import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import test from 'node:test';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
function fixture(){
 const fields={qualitySearch:{value:''},qualityVariables:{querySelectorAll:()=>[]},qualityFailuresHeading:{},
  qualityFailurePager:{classList:{toggle(){}}},qualityFailuresPrevious:{},qualityFailuresNext:{},qualityFailuresPage:{},qualityFailures:{}};
 const state={qualityVariable:'',qualityResult:{matchingFailures:1,failurePage:1,failurePageSize:20,variables:[{key:'width',name:'Width',processCode:'Press',operationSeq:10,checked:3,failed:1,failureRate:100/3,lastFailure:null}],
  failures:[{id:'failure',jobNum:'J1',inspectionPhase:'In Process',number:6,resourceId:'M1',at:'2026-10-18',processCode:'Press',operationSeq:10,operator:'Tech',shift:'5 Day',machineCounter:300,
    category:'Tooling',cause:'Worn <insert>',action:'Replaced insert',reason:'Stopped for repair',detail:'Width 21 above USL 20',variableKeys:['width'],failedReadings:[{variable:'Width',value:21,unit:'mm'}]}]}};
 const c=vm.createContext({state,$:id=>fields[id],escapeHtml:value=>String(value).replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;'),formatDateTime:String});
 for(const name of ['qualityPercent','renderQualityVariables','renderQualityFailures']){
  const start=source.indexOf(`function ${name}(`);vm.runInContext(source.slice(start,source.indexOf('\nfunction ',start+1)),c);
 }
 return {c,state,fields};
}
test('Quality shows zero versus unavailable rates distinctly, without changing counts',()=>{
 const {c,fields}=fixture();assert.equal(c.qualityPercent(null),'-');assert.equal(c.qualityPercent(0),'0%');
 c.renderQualityVariables();assert.match(fields.qualityVariables.innerHTML,/Width/);assert.match(fields.qualityVariables.innerHTML,/33\.33%/);
 fields.qualitySearch.value='other';c.renderQualityVariables();assert.match(fields.qualityVariables.innerHTML,/No matching variables/);
});
test('Failure drilldown preserves cause, action and notes, escaping operator text',()=>{
 const {c,fields,state}=fixture();state.qualityVariable='width';c.renderQualityFailures();
 assert.match(fields.qualityFailuresHeading.textContent,/Width.*\(1\)/);
 for(const text of ['Worn &lt;insert&gt;','Replaced insert','Stopped for repair','Width 21 above USL 20','Job J1'])assert.ok(fields.qualityFailures.innerHTML.includes(text));
 assert.ok(!fields.qualityFailures.innerHTML.includes('Worn <insert>'));
 assert.match(fields.qualityFailures.innerHTML,/<article class="quality-failure">/);
 assert.match(fields.qualityFailures.innerHTML,/<dt>Inspection Phase<\/dt><dd>In Process<\/dd>/);
 assert.ok(!fields.qualityFailures.innerHTML.includes('<details'));
 state.qualityResult.failures[0].recordedDetail='Original failure description';c.renderQualityFailures();
 assert.ok(fields.qualityFailures.innerHTML.includes('Original failure description'));
 state.qualityResult.variables.push({key:'angle',name:'Angle'});state.qualityVariable='angle';c.renderQualityFailures();
 assert.match(fields.qualityFailures.innerHTML,/No recorded failures/);
});

test('No variable selected never displays all failures, even if a stale payload contains them',()=>{
 const {c,fields}=fixture();c.renderQualityFailures();
 assert.match(fields.qualityFailures.innerHTML,/No variable selected/);
 assert.ok(!fields.qualityFailures.innerHTML.includes('Worn'));
});

test('Equivalent violation descriptions display once without repeated variable prefixes',()=>{
 const {c,fields,state}=fixture();state.qualityVariable='width';
 const row=state.qualityResult.failures[0];
 row.detail='Width: Width: entered value 21 is above USL 20.';
 row.recordedDetail='Width: entered value 21 is above USL 20.';
 c.renderQualityFailures();
 assert.ok(!fields.qualityFailures.innerHTML.includes('<dt>Failure Description</dt>'));
 assert.match(fields.qualityFailures.innerHTML,/<dt>Violation<\/dt><dd>entered value 21 is above USL 20\.<\/dd>/);
 row.recordedDetail='Additional recorded information';c.renderQualityFailures();
 assert.ok(fields.qualityFailures.innerHTML.includes('Additional recorded information'));
});

test('Pager represents the matching variable count rather than the overall part failure count',()=>{
 const {c,fields,state}=fixture();state.qualityVariable='width';state.qualityResult.matchingFailures=43;state.qualityResult.failurePage=2;
 c.renderQualityFailures();assert.equal(fields.qualityFailuresPage.textContent,'21-40 of 43');
 assert.equal(fields.qualityFailuresPrevious.disabled,false);assert.equal(fields.qualityFailuresNext.disabled,false);
 state.qualityResult.failurePage=3;c.renderQualityFailures();assert.equal(fields.qualityFailuresPage.textContent,'41-43 of 43');assert.equal(fields.qualityFailuresNext.disabled,true);
});
