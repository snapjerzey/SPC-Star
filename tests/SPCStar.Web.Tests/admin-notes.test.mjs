import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
test('operator history brings replied notes forward without altering original dates or saved order',()=>{
 const entries=[
  {entryType:'PhaseComplete',timestamp:'2026-10-21T12:00:00Z'},
  {entryType:'Note',timestamp:'2026-10-20T12:00:00Z',noteReplies:[{timestamp:'2026-10-21T13:00:00Z'}]},
  {entryType:'Note',timestamp:'2026-10-19T12:00:00Z',noteReplies:[]}
 ];
 const original=JSON.stringify(entries);let displayed;
 const c=vm.createContext({$:()=>({}),renderHistoryList:(_,rows)=>{displayed=rows;}});
 const start=source.indexOf('function renderJobNotes(');
 vm.runInContext(source.slice(start,source.indexOf('\nfunction ',start+1)),c);
 c.renderJobNotes(entries);
 assert.equal(displayed[0],entries[1]);assert.equal(displayed[1],entries[0]);assert.equal(displayed[2],entries[2]);
 assert.equal(JSON.stringify(entries),original);
 c.renderJobNotes([]);assert.equal(displayed.length,0);
});
function fixture(api) {
 const nodes=new Map();const $=id=>{
  if(!nodes.has(id)){const classes=new Set();nodes.set(id,{value:'',innerHTML:'',textContent:'',replaceChildren(){this.innerHTML='';},classList:{add:x=>classes.add(x),toggle:(x,on)=>on?classes.add(x):classes.delete(x)}});}
  return nodes.get(id);
 };
 const c=vm.createContext({state:{},$,api,canViewAdminNotes:()=>true,
  formatDateTime:String,escapeHtml:value=>String(value).replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;'),
  adminNotesQuery:page=>'page='+page,readableError:e=>e.message,Math});
 const repliesStart=source.indexOf('function renderNoteReplies(');vm.runInContext(source.slice(repliesStart,source.indexOf('\nasync function ',repliesStart)),c);
 const start=source.indexOf('async function loadAdminNotes(');vm.runInContext(source.slice(start,source.indexOf('\nfunction ',start)),c);
 return {c,$};
}
test('Admin Notes preserves original text and offers only an append-only response form',async()=>{
 const {c,$}=fixture(async()=>({entries:[{timestamp:'today',jobNum:'J1',partNum:'70311',resourceId:'4S-1',operatorUserId:'operator',noteText:'First line\n<script>unsafe</script>'}],total:1,page:1,pageSize:50}));
 await c.loadAdminNotes();const html=$('adminNotesRows').innerHTML;
 for(const value of ['J1','70311','4S-1','operator','First line\n&lt;script&gt;unsafe&lt;/script&gt;'])assert(html.includes(value));
 assert.doesNotMatch(html,/<script>|<input|<select/);assert.match(html,/admin-note-original/);assert.match(html,/admin-note-reply-form/);assert.equal($('adminNotesMessage').textContent,'1-1 of 1 notes');
});
test('empty and failed queries never retain previous note rows',async()=>{
 const {c,$}=fixture(async()=>({entries:[],total:0,page:1,pageSize:50}));$('adminNotesRows').innerHTML='old';
 await c.loadAdminNotes();assert.equal($('adminNotesRows').innerHTML,'');assert.equal($('adminNotesMessage').textContent,'No notes found.');
 c.api=async()=>{throw Error('Failed request');};$('adminNotesRows').innerHTML='old';await c.loadAdminNotes();
 assert.equal($('adminNotesRows').innerHTML,'');assert.equal($('adminNotesMessage').textContent,'Failed request');
});
test('late responses cannot overwrite a newer Notes filter result',async()=>{
 const waiting=[];const {c,$}=fixture(()=>new Promise(resolve=>waiting.push(resolve)));
 const first=c.loadAdminNotes(1),second=c.loadAdminNotes(2);
 waiting[1]({entries:[],total:0,page:1,pageSize:50});await second;
 waiting[0]({entries:[{timestamp:'old',jobNum:'old'}],total:1,page:1,pageSize:50});await first;
 assert.equal($('adminNotesRows').innerHTML,'');assert.equal($('adminNotesMessage').textContent,'No notes found.');
});
test('central Notes API authenticates both reads and append-only replies, without editing or deletion',()=>{
 const endpoints=fs.readFileSync(new URL('../../src/SPCStar.Api/JobNotesEndpoints.cs',import.meta.url),'utf8');
 assert.match(endpoints,/MapGet\("\/admin\/job-notes"/);assert.match(endpoints,/ValidateSession/);assert.match(endpoints,/CanViewChangeHistory/);
 assert.match(endpoints,/MapPost\("\/admin\/job-notes\/\{noteId:guid\}\/replies"/);
 assert.doesNotMatch(endpoints,/MapPut|MapPatch|MapDelete/);
});

test('reply rendering escapes text and identifies the responder and timestamp',()=>{
 const {c}=fixture(async()=>{});
 const result=c.renderNoteReplies([{userName:'QA <test>',timestamp:'today',replyText:'Fixed <script>\nSecond line'}]);
 assert(result.includes('QA &lt;test&gt;'));assert(result.includes('Fixed &lt;script&gt;\nSecond line'));assert(result.includes('today'));assert.doesNotMatch(result,/<script>/);
});

test('Replied by is separate from Posted by and is included in the combined Notes query',()=>{
 const values={adminNotesUser:' operator ',adminNotesRepliedBy:' QAUser ',adminNotesSearch:'',adminNotesPart:'70311',adminNotesJob:'',adminNotesMachine:''};
 const c=vm.createContext({$:id=>({value:values[id]||''}),dateTimeLocalValue:()=>null,URLSearchParams});
 const start=source.indexOf('function adminNotesQuery(');
 vm.runInContext(source.slice(start,source.indexOf('\nfunction ',start+1)),c);
 const query=new URLSearchParams(c.adminNotesQuery(2));
 assert.equal(query.get('repliedBy'),'QAUser');assert.equal(query.get('userName'),'operator');assert.equal(query.get('page'),'2');
 values.adminNotesRepliedBy='';assert.equal(new URLSearchParams(c.adminNotesQuery()).has('repliedBy'),false);
});
