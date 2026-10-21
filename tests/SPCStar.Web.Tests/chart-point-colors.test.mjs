import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import test from 'node:test';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
function context(){
 const c=vm.createContext({});
 for(const name of ['chartPointColor','drawLineSeries']){
  const start=source.indexOf(`function ${name}(`);vm.runInContext(source.slice(start,source.indexOf('\nfunction ',start+1)),c);
 }
 return c;
}
test('Chart colors distinguish Watch from failed entries, with failure taking precedence',()=>{
 const c=context();
 assert.equal(c.chartPointColor({}),'#0f63b8');
 assert.equal(c.chartPointColor({hasRuleViolation:true,ruleViolations:['CustomRuleTriggered']}),'#0f63b8');
 assert.equal(c.chartPointColor({isWatch:true}),'#e0a000');
 assert.equal(c.chartPointColor({isWatch:true,isFailed:true}),'#b42318');
 assert.equal(c.chartPointColor({ruleViolations:['AttributeRejected']}),'#b42318');
 assert.equal(c.chartPointColor({ruleViolations:['SpecLimitViolation']}),'#b42318');
 assert.equal(c.chartPointColor({},'#344054'),'#344054');
});
test('Line-series dots use actual status rather than the generic violation flag',()=>{
 const c=context(),fills=[];
 const ctx={beginPath(){},moveTo(){},lineTo(){},stroke(){},arc(){},fill(){fills.push(this.fillStyle);}};
 c.drawLineSeries(ctx,[{}, {isWatch:true}, {isFailed:true}],(_,i)=>i,()=>1);
 assert.deepEqual(fills,['#0f63b8','#e0a000','#b42318']);
});
