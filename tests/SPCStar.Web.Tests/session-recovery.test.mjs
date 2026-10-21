import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
function extract(name) {
 const start=source.indexOf(`function ${name}(`);
 const end=source.slice(start+1).search(/\n(?:async )?function /);
 return (source.slice(start-6,start)==='async '?'async ':'')+source.slice(start,end<0?undefined:start+1+end);
}
function fixture(responses) {
 const nodes=new Map(),state={user:{userName:'operator',sessionToken:'token'},currentShift:'Days'};
 const $=id=>{if(!nodes.has(id))nodes.set(id,{value:'kept',textContent:'',className:'',focus(){},showModal(){this.open=true;},close(){this.open=false;},querySelector(){return {disabled:false};}});return nodes.get(id);};
 const calls=[];
 const c=vm.createContext({state,$,fetch:async(...args)=>{calls.push(args);const response=responses.shift();if(response instanceof Error)throw response;return response;},
  clearSavedSession(){},saveCurrentWorkContext(){},saveInspectionDrafts(){},saveRefreshSession(){},setStatus(){},formatRoleList:()=>'',configureSetupAccess(){},hasPermission:()=>true,setupSectionAccess:()=>true,showPanel(){},readableError:e=>e.message});
 for(const name of ['sessionFetch','requestSessionSignIn','renewSession'])vm.runInContext(extract(name),c);
 return {c,state,$,calls};
}
test('invalid-session response opens sign-in without clearing current form entries or replaying requests',async()=>{
 const {c,state,$,calls}=fixture([{status:401}]);$('materialLot').value='original lot';
 await c.sessionFetch('/material-changes',{method:'POST'});
 assert.equal(state.sessionInvalid,true);assert.equal($('sessionSignInDialog').open,true);assert.equal($('materialLot').value,'original lot');
 await assert.rejects(c.sessionFetch('/material-changes',{method:'POST'}),/Sign in again/);assert.equal(calls.length,1);
});
test('actual permission denial does not ask a valid user to sign in again',async()=>{
 const {c,state,$}=fixture([{status:403},{status:200}]);await c.sessionFetch('/admin/job-notes');
 assert.equal(state.sessionInvalid,undefined);assert.notEqual($('sessionSignInDialog').open,true);
});
test('legacy forbidden response checks the session before requesting sign-in',async()=>{
 const {c,state}=fixture([{status:403},{status:401}]);await c.sessionFetch('/admin/job-notes');assert.equal(state.sessionInvalid,true);
});
test('wrong login credentials and a network outage are not session expiry',async()=>{
 const {c,state}=fixture([{status:401},new TypeError('Network failure')]);await c.sessionFetch('/auth/login');
 await assert.rejects(c.sessionFetch('/material-changes'),/Network failure/);assert.equal(state.sessionInvalid,undefined);
});
test('successful reauthentication preserves forms and replaces only the login session',async()=>{
 const {c,state,$,calls}=fixture([{ok:true,json:async()=>({userName:'operator',sessionToken:'new-token',roles:['Operator']})}]);
 state.sessionInvalid=true;$('materialLot').value='original lot';$('sessionSignInDialog').open=true;
 await c.renewSession({preventDefault(){}});
 assert.equal(state.user.sessionToken,'new-token');assert.equal(state.sessionInvalid,false);assert.equal($('materialLot').value,'original lot');assert.equal($('sessionSignInDialog').open,false);assert.equal(calls.length,1);
});
test('failed reauthentication leaves unsaved entries and the sign-in dialog intact',async()=>{
 const {c,state,$}=fixture([{ok:false,status:401}]);state.sessionInvalid=true;$('materialLot').value='original lot';$('sessionSignInDialog').open=true;
 await c.renewSession({preventDefault(){}});assert.equal(state.sessionInvalid,true);assert.equal($('sessionSignInDialog').open,true);assert.equal($('materialLot').value,'original lot');assert.match($('sessionSignInMessage').textContent,/Incorrect password/);
});
