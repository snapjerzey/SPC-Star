import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
function setup(permissions){
  const nodes=new Map();
  const get=id=>{if(!nodes.has(id)) {const classes=new Set();nodes.set(id,{classes,classList:{toggle(name,on){on?classes.add(name):classes.delete(name)},add(name){classes.add(name)},remove(name){classes.delete(name)}}});}return nodes.get(id);};
  const c=vm.createContext({state:{user:{permissions},setupSection:'Inspection'},$:get,document:{querySelectorAll:()=>[]},seedHistoryFiltersFromWorkContext(){},applyHistoryFilters(){},syncHistoryFiltersFrom(){},loadChangeHistory(){},loadAdminNotes(){}});
  for(const name of ['hasPermission','canViewChangeHistory','canViewAdminNotes','canAccessAdmin','canManageSetup','canManageInspectionSetup','canManageMachines','canManageUsers','canManageRules','canImportSetupData','canArchiveData','canViewHistory','canAccessSetup','canAccessNavigation','sectionsForPanel','panelForSection','setupSectionAccess','defaultSetupSection','setSetupSectionVisibility','configureSetupAccess','setMainPanelVisibility','showSetupSection','showPanel']){
    const start=source.indexOf(`function ${name}(`);vm.runInContext(source.slice(start,source.indexOf('\nfunction ',start+1)),c);
  }
  return {c,get};
}
test('QA sees Setup Data and Change History but cannot enter Archive',()=>{
  const {c,get}=setup(['CanEnterInspections','CanManageInspectionPlans','CanManageUsers','CanExportQAData','CanViewChangeHistory']);
  c.configureSetupAccess();assert.equal(get('adminTab').classes.has('hidden'),false);
  assert.equal(get('dataTab').classes.has('hidden'),false);
  c.showPanel('data');assert.equal(c.state.setupSection,'Ledger');
  for(const view of ['Ledger','Quality','Charts','Issues']) {
    c.showSetupSection(view);
    assert.equal(c.state.historyView,view);
    assert(!get(`history${view}View`).classes.has('hidden'));
    assert(get(`history${view}Tab`).classes.has('active'));
  }
  assert.equal(get('dataPanel').classes.has('hidden'),false);
  assert.equal(get('setupPanel').classes.has('hidden'),true);
  c.showSetupSection('Cert');c.showPanel('setup');assert.equal(c.state.setupSection,'Inspection');
  c.showPanel('data');assert.equal(c.state.setupSection,'Cert');
  c.showPanel('admin');assert.equal(c.state.setupSection,'ChangeHistory');
  c.showSetupSection('Archive');assert.equal(c.state.setupSection,'ChangeHistory');
});
test('System Managers have Archive only under Admin',()=>{
  const {c,get}=setup(['CanManageInspectionPlans','CanManageUsers','CanExportQAData','CanUseGodMode']);
  c.configureSetupAccess();assert.equal(get('adminTab').classes.has('hidden'),false);
  c.showPanel('admin');assert.equal(c.state.setupSection,'Archive');
  assert.equal(get('adminPanel').classes.has('hidden'),false);
  assert.equal(get('dataPanel').classes.has('hidden'),true);
  assert.deepEqual(Array.from(c.sectionsForPanel('setup')),['Inspection','Users','Machines','Rules']);
  assert.deepEqual(Array.from(c.sectionsForPanel('data')),['Ledger','Quality','Charts','Issues','Cert']);
  assert.deepEqual(Array.from(c.sectionsForPanel('admin')),['Archive','ChangeHistory','Notes']);
  c.showSetupSection('ChangeHistory');assert.equal(c.state.setupSection,'ChangeHistory');
  c.showSetupSection('Notes');assert.equal(c.state.setupSection,'Notes');
});
test('Operator and Line Tech cannot open Setup Data or Admin',()=>{
  const {c,get}=setup(['CanEnterInspections','CanOverrideDriftLock']);
  c.configureSetupAccess();for(const tab of ['setupTab','dataTab','adminTab'])assert(get(tab).classes.has('hidden'));
  for(const panel of ['setup','data','admin']){c.showPanel(panel);assert.equal(c.state.activePanel,'inspect');}
});
test('Data-only access does not expose Setup; signed-out navigation is ignored',()=>{
  const {c,get}=setup(['CanExportQAData']);c.configureSetupAccess();
  assert(get('setupTab').classes.has('hidden'));assert(!get('dataTab').classes.has('hidden'));
  c.showPanel('data');assert.equal(c.state.activePanel,'data');
  c.state.user=null;c.showPanel('admin');assert.equal(c.state.activePanel,'data');
});
