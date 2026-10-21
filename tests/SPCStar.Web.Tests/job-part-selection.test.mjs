import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import test from 'node:test';

const source = fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js', import.meta.url), 'utf8');
function fixture() {
  const fields = { jobNum: { value: '' }, partNum: { value: '' },
    workJobOptions: {}, workPartOptions: {}, jobOptions: { values: ['unfiltered'] }, partOptions: { values: ['unfiltered'] } };
  const context = vm.createContext({
    state: { snapshot: {
      parts: ['61033', '61131', '610330'].map(partNum => ({ partNum })),
      jobs: [ { jobNum: '1', partNum: '61033' }, { jobNum: '10', partNum: '61131' },
        { jobNum: '11', partNum: '61033' }, { jobNum: 'OTHER', partNum: '610330' } ]
    } },
    $: id => fields[id], fillDatalist: (field, rows, valueOf) => { field.values = Array.from(rows, valueOf); },
    refreshOperationChoices: () => {}, refreshMachineDropdowns: () => {}
  });
  for (const name of ['workJobPartChoices', 'refreshWorkJobPartChoices', 'updateJobFromPart',
    'updatePartFromJob', 'workJobPartMismatchMessage']) {
    const start = source.indexOf(`function ${name}(`);
    vm.runInContext(source.slice(start, source.indexOf('\nfunction ', start + 1)), context);
  }
  return { context, fields };
}

test('part selection restricts jobs exactly, including part numbers that are prefixes of others', () => {
  const { context, fields } = fixture();
  fields.partNum.value = ' 61033 ';
  context.refreshWorkJobPartChoices();
  assert.deepEqual(fields.workJobOptions.values, ['1', '11']);
  assert.deepEqual(fields.jobOptions.values, ['unfiltered']);
  assert.deepEqual(fields.partOptions.values, ['unfiltered']);
  fields.partNum.value = '611';
  context.refreshWorkJobPartChoices();
  assert.deepEqual(fields.workJobOptions.values, ['10']);
  fields.partNum.value = '';
  context.refreshWorkJobPartChoices();
  assert.equal(fields.workJobOptions.values.length, 4);
});

test('known job selection fills its part and restricts the part list to that part', () => {
  const { context, fields } = fixture();
  fields.jobNum.value = '10';
  context.updatePartFromJob();
  assert.equal(fields.partNum.value, '61131');
  assert.deepEqual(fields.workPartOptions.values, ['61131']);
  assert.deepEqual(fields.workJobOptions.values, ['10']);
  fields.jobNum.value = 'other';
  context.updatePartFromJob();
  assert.equal(fields.partNum.value, '610330');
  assert.deepEqual(fields.workPartOptions.values, ['610330']);
});

test('partial jobs narrow parts, while unknown new jobs keep valid parts available', () => {
  const { context, fields } = fixture();
  fields.jobNum.value = 'o';
  context.refreshWorkJobPartChoices();
  assert.deepEqual(fields.workPartOptions.values, ['610330']);
  fields.jobNum.value = 'NEW-JOB';
  context.refreshWorkJobPartChoices();
  assert.equal(fields.workPartOptions.values.length, 3);
  assert.equal(context.workJobPartMismatchMessage('NEW-JOB', '61131'), '');
});

test('changing part clears an incompatible job but preserves compatible and new jobs', () => {
  const { context, fields } = fixture();
  fields.jobNum.value = 'OTHER';
  fields.partNum.value = '61033';
  context.updateJobFromPart();
  assert.equal(fields.jobNum.value, '');
  fields.jobNum.value = '11';
  context.updateJobFromPart();
  assert.equal(fields.jobNum.value, '11');
  fields.partNum.value = '61131';
  context.updateJobFromPart();
  assert.equal(fields.jobNum.value, '');
  fields.jobNum.value = 'NEW';
  context.updateJobFromPart();
  assert.equal(fields.jobNum.value, 'NEW');
});

test('load rejects manually typed mismatches and filtering runs on both field edits', () => {
  const { context } = fixture();
  assert.match(context.workJobPartMismatchMessage(' 1 ', '61131'), /Job 1 belongs to part 61033/);
  assert.equal(context.workJobPartMismatchMessage('OTHER', ' 610330 '), '');
  const load = source.slice(source.indexOf('async function loadContext('), source.indexOf('function clearWorkContext('));
  assert.ok(load.includes('workJobPartMismatchMessage(jobNum, partNum)'));
  assert.ok(load.indexOf('workJobPartMismatchMessage(jobNum, partNum)') < load.indexOf('loadVariableContexts('));
  assert.match(source, /\$\("partNum"\)\.addEventListener\("input", \(\) => \{\s+updateJobFromPart\(\)/);
  assert.match(source, /\$\("jobNum"\)\.addEventListener\("input", \(\) => \{\s+updatePartFromJob\(\)/);
});
