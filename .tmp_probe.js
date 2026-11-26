const fs = require('fs');
const f = 'C:/Users/Meditech_01/.qoder/projects/C--Users-Meditech-01-Documents-Qoder-2026-10-30-bed8e773/bed8e773-35e4-4af4-9cba-3b81f077d980.jsonl';
const s = fs.readFileSync(f, 'utf8');
console.log(s.slice(240300, 242600).replace(/\n/g, '\n'));
