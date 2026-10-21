import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
function fixture(type='reload',valid=true) {
 const values=new Map([['spc-star-session',JSON.stringify({userName:'tester',sessionToken:'real-token',shift:'5 Day'})]]);
 const calls=[],nodes=new Map();
 const c=vm.createContext({SESSION_STORAGE_KEY:'spc-star-session',state:{},performance:{getEntriesByType:()=>[{type}]},
  window:{localStorage:{removeItem(){}},sessionStorage:{getItem:key=>values.get(key),setItem:(k,v)=>values.set(k,v),removeItem:k=>values.delete(k)}},
  document:{body:{classList:{add(){}}}},$:id=>{if(!nodes.has(id))nodes.set(id,{classList:{add(){},remove(){}},textContent:''});return nodes.get(id);},setStatus(){},
  api:async(path,options)=>{calls.push({path,options});if(!valid)throw Error('Invalid session');return {userName:'tester',sessionToken:'real-token',roles:['Operator'],permissions:['CanEnterInspections']};},
  startAuthenticatedSession:async()=>calls.push('started')});
 for(const name of ['clearSavedSession','saveRefreshSession','resumeRefreshSession']) {
  const start=source.indexOf(`function ${name}(`),end=source.indexOf('\nfunction ',start+1);
  const asyncPrefix=name==='resumeRefreshSession'?'async ':'';
  vm.runInContext(asyncPrefix+source.slice(start,end),c);
 }
 return {c,calls,values};
}
test('refresh validates the existing token before resuming; does not trust stored permissions',async()=>{
 const {c,calls}=fixture();await c.resumeRefreshSession();
 assert.equal(calls[0].options.headers['X-SPCStar-Session'],'real-token');
 assert.equal(calls[1],'started');assert.equal(c.state.currentShift,'5 Day');
});
test('fresh navigation always requires login even if tab storage exists',async()=>{
 const {c,calls,values}=fixture('navigate');await c.resumeRefreshSession();assert.equal(calls.length,0);assert.equal(values.size,0);
});
test('invalid or server-restarted session returns to login without entering inspection',async()=>{
 const {c,calls,values}=fixture('reload',false);await c.resumeRefreshSession();assert.equal(c.state.user,null);assert(!calls.includes('started'));assert.equal(values.size,0);
});
test('sign out removes the tab session',()=>{
 const {c,values}=fixture();c.clearSavedSession();assert.equal(values.size,0);
});
