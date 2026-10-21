import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

const html = fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/index.html', import.meta.url), 'utf8');
const sections = ['sopEnglishBody', 'sopSpanishBody', 'lineTechSopEnglishBody', 'lineTechSopSpanishBody', 'qaSopEnglishBody', 'qaSopSpanishBody'];

test('all six role/language SOPs include current correction, failure, entry, and history workflows', () => {
  for (const id of sections) {
    const start = html.indexOf(`<div id="${id}"`);
    assert.notEqual(start, -1, id);
    const end = html.indexOf('</div>', start);
    const body = html.slice(start, end);
    for (const term of ['2026', 'User Error', 'Machine', 'Tooling', 'Material', 'Clear Lock', 'End Failed Inspection']) {
      assert.ok(body.includes(term), `${id}: missing ${term}`);
    }
    assert.equal((body.match(/<ol>/g) || []).length, (body.match(/<\/ol>/g) || []).length, `${id}: lists`);
    assert.equal((body.match(/<li>/g) || []).length, (body.match(/<\/li>/g) || []).length, `${id}: list items`);
    assert.equal((body.match(/<p>/g) || []).length, (body.match(/<\/p>/g) || []).length, `${id}: paragraphs`);
    assert.ok(!body.includes('The lock-clear record becomes part of the job history'));
    assert.ok(!body.includes('El registro de liberación del bloqueo pasa a formar parte'));
    assert.ok(!body.includes('método de medición, deriva del proceso u otra causa'));
    assert.doesNotMatch(body, /Archon|System Manager|system administrator/i, `${id}: private account/access details`);
    if (!id.startsWith('qaSop')) {
      assert.doesNotMatch(body, /Data &gt; (?:Ledger|Part Quality|Charts|Top Issues)|Export XLSX|Edit Lot/i, `${id}: no reporting permissions for production roles`);
      assert.ok(body.includes('Job History'), `${id}: inspection-side notes and responses`);
    }
    if (id === 'sopEnglishBody' || id === 'sopSpanishBody') {
      assert.doesNotMatch(body, /70306|Jaw Tab Center|Weld Nub|Keyence|Ledger|two or three digits|dos o tres dígitos/);
      const words = body.replace(/<[^>]*>/g, ' ').trim().split(/\s+/).length;
      assert.ok(words >= 800 && words < 2400, `${id}: provide a complete, readable walkthrough`);
      assert.doesNotMatch(body, /Ledger|Setup &gt; History/i, `${id}: operators do not have Data reporting access`);
      for (const term of ['Sign In', 'Load', 'Save Job Data', 'Machine Counter', 'Box #', 'Accept', 'Reject', 'Submit Inspection', 'Save Material Lot', 'Submit Note', 'Sign Out']) {
        assert.ok(body.includes(term), `${id}: missing ${term}`);
      }
    }
    const filenames = {
      sopEnglishBody: 'operator-en.md', sopSpanishBody: 'operator-es.md',
      lineTechSopEnglishBody: 'line-tech-en.md', lineTechSopSpanishBody: 'line-tech-es.md',
      qaSopEnglishBody: 'qa-en.md', qaSopSpanishBody: 'qa-es.md'
    };
    const reference = fs.readFileSync(new URL(`../../docs/sops/${filenames[id]}`, import.meta.url), 'utf8');
    for (const match of body.matchAll(/<(?:h3|p|li)>([\s\S]*?)<\/(?:h3|p|li)>/g)) {
      const text = match[1].replace(/<[^>]*>/g, '').replace(/&gt;/g, '>').replace(/&amp;/g, '&')
        .replace(/&#39;/g, "'").replace(/&quot;/g, '"').replace(/&lt;/g, '<').replace(/\s+/g, ' ').trim();
      assert.ok(reference.includes(text), `${id}: stale project reference: ${text.slice(0, 70)}`);
    }
  }
});

test('all role/language SOPs omit certificate workflows for this update', () => {
  for (const id of sections) {
    const start = html.indexOf(`<div id="${id}"`);
    const body = html.slice(start, html.indexOf('</div>', start));
    assert.doesNotMatch(body, /Weld Pin|Keyence|Preview COAs|QF-E11[23]|certificat|certificad/i, id);
  }
});

test('all role SOPs explain persistent lots, phase sequencing and submission confirmation', () => {
  for (const id of sections) {
    const start = html.indexOf(`<div id="${id}"`);
    const body = html.slice(start, html.indexOf('</div>', start));
    for (const term of ['Current lot', 'Material issue at job start', 'Submit Inspection', 'Sign Out']) {
      assert.ok(body.includes(term), `${id}: missing ${term}`);
    }
    assert.ok((body.match(/<h3>/g) || []).length >= 10, `${id}: missing workflow sections`);
    assert.doesNotMatch(body, /retained lock events orange|lockouts, lock-clear actions|drift, or rejected-attribute events/i);
  }
});

test('QA SOPs cover current review, rule configuration and maintenance controls', () => {
  for (const id of ['qaSopEnglishBody', 'qaSopSpanishBody']) {
    const start = html.indexOf(`<div id="${id}"`);
    const body = html.slice(start, html.indexOf('</div>', start));
    for (const term of ['Part Quality', 'Ledger', 'Job Notes', 'Top Issues', 'Export XLSX',
      'Cp/Cpk', 'Pp/Ppk', 'Save Custom Protocol', 'Directional Drift', 'Near Specification Limits',
      'Repeated Near-Limit Inspections', 'Normal completed inspections before OK', 'Save Part Inspection',
      'Save User', 'Save Machine', 'Edit Lot', 'Admin', 'Notes', 'Replied by', 'Send Response',
      'Change History', 'Show Changes', 'Export CSV', 'QA - Read only']) {
      assert.ok(body.includes(term), `${id}: missing ${term}`);
    }
  }
});

test('all SOPs cover actual counter offsets and session recovery without automatic resubmission', () => {
  for (const id of sections) {
    const start = html.indexOf(`<div id="${id}"`);
    const body = html.slice(start, html.indexOf('</div>', start));
    for (const term of ['October 21, 2026', 'Sign In Required', '2,100', '4,100', '8,100'].filter(term => !term.startsWith('October') || id.endsWith('EnglishBody'))) {
      assert.ok(body.includes(term), `${id}: missing ${term}`);
    }
  }
});
