import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
const html=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/index.html',import.meta.url),'utf8');
function fn(name){const start=source.indexOf(`function ${name}(`);return source.slice(start,source.indexOf('\nfunction ',start+1));}
test('unitless count fields and retired hidden timing fields do not block spec edits',()=>{
  assert.doesNotMatch(fn('setupVariableRowTemplate'),/class="setup-unit" required/);
  assert.match(fn('updateSetupVariableType'),/unit.required = false/);
  for(const id of ['setupSampleSize','setupFrequencyValue']){
    const input=html.match(new RegExp(`<input[^>]*id="${id}"[^>]*>`))[0];
    assert.match(input,/disabled/);assert.doesNotMatch(input,/required/);
  }
  assert.match(fn('setupVariableRowTemplate'),/class="setup-usl" type="number" step="any"/);
});
test('removed changelog has no markup, requests or rendering code',()=>{
  assert.doesNotMatch(html,/partChangeHistory|part-change-history/);
  assert.doesNotMatch(source,/part-change-history|loadPartChangeHistory|renderPartChangeHistory/);
  assert.match(source,/"X-SPCStar-User": state.user.userName/);
  assert.match(source,/"X-SPCStar-Session": state.user.sessionToken/);
});

test('a USL-only edit never submits order or overwrites phase-specific specifications',()=>{
  const c=vm.createContext({});
  for(const name of ['setupVariableEditFields','setupVariableHasEdits']) vm.runInContext(fn(name),c);
  const original={characteristicName:'Height',characteristicType:'Variable',nominal:null,lsl:'0.30',usl:'0.32',unitOfMeasure:'in',location:'Upper',inspectionMethod:'Caliper',displayOrder:22,lcl:null,ucl:null};
  const edited={...original,usl:'0.31'};
  const plan={...original,lsl:.29,usl:.32,displayOrder:17};
  const request=c.setupVariableEditFields(edited,original,plan);
  assert.equal(request.usl,'0.31');assert.equal(request.lsl,.29);
  assert.equal('displayOrder' in request,false);
  assert.equal(c.setupVariableHasEdits(original,original),false);
  assert.equal(c.setupVariableHasEdits(edited,original),true);
  assert.match(source,/if \(state.setupSaving\) return/);
  assert.match(source,/button.textContent = "Saving\.\.\."/);
});
