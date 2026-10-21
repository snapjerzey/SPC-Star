import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const modulePath = process.env.SPCSTAR_MARKED_MODULE;
const { marked } = await import(modulePath ? pathToFileURL(modulePath).href : 'marked');
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const sections = {
  sopEnglishBody: 'operator-en.md', sopSpanishBody: 'operator-es.md',
  lineTechSopEnglishBody: 'line-tech-en.md', lineTechSopSpanishBody: 'line-tech-es.md',
  qaSopEnglishBody: 'qa-en.md', qaSopSpanishBody: 'qa-es.md'
};
const content = {};
for (const [id, filename] of Object.entries(sections)) {
  const tokens = marked.lexer(fs.readFileSync(path.join(root, 'docs/sops', filename), 'utf8'));
  const body = tokens.filter(t => t.type !== 'heading' || t.depth !== 1);
  for (const token of body) {
    if (token.type === 'heading') token.depth = 3;
  }
  content[id] = marked.parser(body);
}
const output = path.join(root, '.tmp/sop-content.json');
fs.mkdirSync(path.dirname(output), { recursive: true });
fs.writeFileSync(output, JSON.stringify(content, null, 2));
console.log(output);
