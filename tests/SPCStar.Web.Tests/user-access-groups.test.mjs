import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
const start=source.indexOf('function usersByAccessLevel(');
const c=vm.createContext({});vm.runInContext(source.slice(start,source.indexOf('\nfunction ',start+1)),c);
test('Users are grouped by all five access levels with alphabetical names and no data changes',()=>{
 const users=[{userName:'Zoe',roles:['Operator']},{userName:'Amy',roles:['Operator']},{userName:'Tech',roles:['LineTech']},{userName:'QAUser',roles:['QA']},{userName:'Viewer',roles:['QA - Read only']},{userName:'Manager',roles:['GOD']}];
 const before=JSON.stringify(users),groups=c.usersByAccessLevel(users);
 assert.deepEqual(Array.from(groups,g=>g.label),['Operators','Line Techs','QA','QA - Read only','System Managers']);
 assert.deepEqual(Array.from(groups[0].users,u=>u.userName),['Amy','Zoe']);
 assert.deepEqual(Array.from(groups.slice(1),g=>g.users[0].userName),['Tech','QAUser','Viewer','Manager']);assert.equal(JSON.stringify(users),before);
});
test('Multi-role users appear once under their effective access level',()=>{
 const users=[{userName:'ReadOnly',roles:['Operator','QA','QA - Read only']},{userName:'Manager',roles:['QA - Read only','GOD']},{userName:'QA',roles:['Operator','LineTech','QA']}];
 const groups=c.usersByAccessLevel(users);assert.equal(groups[2].users[0].userName,'QA');assert.equal(groups[3].users[0].userName,'ReadOnly');assert.equal(groups[4].users[0].userName,'Manager');assert.equal(groups.reduce((n,g)=>n+g.users.length,0),users.length);
});
test('Unknown or missing roles remain visible, and role matching is case-insensitive',()=>{
 const groups=c.usersByAccessLevel([{userName:'NoRole'},{userName:'Unknown',roles:['future-role']},{userName:'Known',roles:['operator']}]);
 assert.equal(groups[0].users[0].userName,'Known');assert.equal(groups[5].label,'Other / Unassigned');assert.equal(groups[5].users.length,2);
 assert.equal(c.usersByAccessLevel([]).length,5);
});
