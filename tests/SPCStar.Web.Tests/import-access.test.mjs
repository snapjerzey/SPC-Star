import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
const start=source.indexOf('function canImportSetupData(');
const fn=source.slice(start,source.indexOf('\nfunction ',start+1));
test('only System Manager sessions expose data import controls',()=>{
  for(const [roles,permission,allowed] of [[['QA'],true,false],[['LineTech'],true,false],[['Operator'],true,false],[['GOD'],true,true],[['GOD'],false,false],[[],true,false]]) {
    const c=vm.createContext({state:{user:{roles}},hasPermission:()=>permission});vm.runInContext(fn,c);
    assert.equal(c.canImportSetupData(),allowed);
  }
});
