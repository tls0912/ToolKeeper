// Run with: node --test tests/MarkPad.Tests/PreviewScroll.test.cjs
// Execute the shipped script with a small DOM so deferred Chromium scroll events
// and layout-dependent source mapping can be exercised without a WebView session.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const script = fs.readFileSync(path.join(__dirname, '../../src/MarkPad/Resources/Preview.js'), 'utf8');

function preview({ blocks = [], lineCount = 100, height = 200, documentHeight = 2200, articleBottom = documentHeight, markdown } = {}) {
  const messages = [], frames = [], listeners = new Map(), ids = new Map();
  const window = {
    scrollY: 0,
    markpadConfig: { language: 'en', token: 'test', markdown: markdown ?? Array(lineCount).fill('line').join('\n') },
    chrome: { webview: { postMessage: message => messages.push(message) } },
    addEventListener(name, callback) {
      if (!listeners.has(name)) listeners.set(name, []);
      listeners.get(name).push(callback);
    },
    scrollTo({ top, behavior }) {
      assert.equal(behavior, 'instant');
      window.scrollY = Math.max(0, Math.min(documentHeight - height, top));
    }
  };
  function element(spec = {}) {
    const classes = new Set();
    const result = {
      nodeType: 1, tagName: spec.tagName || 'P', dataset: { sourceLine: String(spec.line || 1) },
      parentElement: spec.parent || null, hidden: !!spec.hidden, folded: !!spec.folded,
      id: spec.id || '', top: spec.top || 0, bottom: spec.bottom ?? (spec.top || 0) + 100,
      classList: { add: value => classes.add(value), remove: value => classes.delete(value), contains: value => classes.has(value), toggle() {} },
      addEventListener() {}, prepend() {}, append() {}, setAttribute() {}, normalize() {}, querySelector() { return null; },
      querySelectorAll() { return []; },
      getClientRects() { return this.hidden ? [] : [this.getBoundingClientRect()]; },
      getBoundingClientRect() { return { top: this.top - window.scrollY, bottom: this.bottom - window.scrollY }; },
      closest(selector) {
        if (selector === '[data-source-line]') return this;
        if (selector === '.fold-hidden') return this.folded ? this : this.parentElement?.closest(selector);
        return null;
      },
      contains(other) {
        for (let ancestor = other; ancestor; ancestor = ancestor.parentElement) if (ancestor === this) return true;
        return false;
      },
      scrollIntoView() { window.scrollY = Math.max(0, Math.min(documentHeight - height, this.top)); }
    };
    if (result.id) ids.set(result.id, result);
    return result;
  }
  const nodes = blocks.map(block => element(block));
  blocks.forEach((block, index) => { if (Number.isInteger(block.parentIndex)) nodes[index].parentElement = nodes[block.parentIndex]; });
  const article = element({ top: 0, bottom: articleBottom });
  article.contains = node => nodes.includes(node);
  article.querySelectorAll = selector => selector === '[data-source-line]' ? nodes : [];
  const document = {
    documentElement: { scrollHeight: documentHeight },
    getElementById: id => id === 'document' ? article : ids.get(id),
    addEventListener() {}, createElement: () => element()
  };
  vm.runInNewContext(script, {
    window, document, innerHeight: height, Node: { ELEMENT_NODE: 1 },
    requestAnimationFrame: callback => frames.push(callback), setTimeout: () => 1, clearTimeout() {}
  });
  return {
    api: window.markpad, nodes, messages, window,
    emit(name, event = {}) { for (const callback of listeners.get(name) || []) callback(event); },
    flush() { while (frames.length) frames.shift()(); },
    scroll(top) { window.scrollY = top; this.emit('scroll'); this.flush(); },
    lastScroll() { return messages.filter(message => message.type === 'scroll').at(-1); }
  };
}

const chapterBlocks = [
  { line: 1, top: 40, bottom: 100 },
  { line: 11, top: 240, bottom: 400 },
  { line: 31, top: 1040, bottom: 1200 },
  { line: 81, top: 1840, bottom: 2000 }
];

test('source interpolation follows rendered blocks rather than overall height ratio', () => {
  const page = preview({ blocks: chapterBlocks });
  page.api.syncScroll(21.5, .1);
  assert.equal(page.window.scrollY, 660);
  assert.equal(page.lastScroll().sourcePosition, 21.5);
  assert.equal(page.lastScroll().scrollProgress, .33);
  assert.equal(page.lastScroll().text, '660');
  assert.equal(page.lastScroll().line, 31); // Preserve the outline's next-visible-block rule.
  assert.equal(page.lastScroll().flag, true);
});

test('both progress endpoints snap exactly, including a bottom-clamped final block', () => {
  const page = preview({ blocks: chapterBlocks });
  page.api.syncScroll(11, 1);
  assert.equal(page.window.scrollY, 2000);
  assert.equal(page.lastScroll().scrollProgress, 1);
  page.api.syncScroll(81, 0);
  assert.equal(page.window.scrollY, 0);
  assert.equal(page.lastScroll().scrollProgress, 0);
});

test('Chromium fractional bottom offsets report exact completed progress', () => {
  const page = preview({ blocks: chapterBlocks });
  page.scroll(1999.667);
  assert.equal(page.lastScroll().scrollProgress, 1);
});

test('host echoes stay flagged across deferred events and user input without movement', () => {
  const page = preview({ blocks: chapterBlocks });
  page.api.syncScroll(21, .5);
  page.emit('scroll');
  page.flush();
  assert.equal(page.lastScroll().flag, true);
  page.emit('wheel');
  page.emit('scroll');
  page.flush();
  assert.equal(page.lastScroll().flag, true);
  page.scroll(740);
  assert.equal(page.lastScroll().flag, false);
  assert.equal(page.lastScroll().sourcePosition, 23.5);
});

test('keyboard and scrollbar movement can take control after each host synchronization', () => {
  const page = preview({ blocks: chapterBlocks });
  for (const [name, event] of [['keydown', { key: 'PageDown' }], ['pointerdown', {}], ['touchstart', {}]]) {
    page.api.syncScroll(21, .5);
    page.emit(name, event);
    page.scroll(840);
    assert.equal(page.lastScroll().flag, false);
    assert.equal(page.lastScroll().sourcePosition, 26);
  }
});

test('native scrollbar movement takes control without DOM pointer or wheel events', () => {
  const page = preview({ blocks: chapterBlocks });
  page.api.syncScroll(21, .5);
  page.scroll(840);
  assert.equal(page.lastScroll().flag, false);
  assert.equal(page.lastScroll().sourcePosition, 26);
});

test('restoring scroll also suppresses delayed events, while explicit anchors release it', () => {
  const page = preview({ blocks: [...chapterBlocks, { line: 91, top: 1900, bottom: 2010, id: 'chapter' }] });
  page.api.restore(720, 0);
  page.emit('scroll');
  page.flush();
  assert.equal(page.window.scrollY, 720);
  assert.equal(page.lastScroll().flag, true);
  page.api.anchor('#chapter');
  assert.equal(page.window.scrollY, 1900);
  assert.equal(page.lastScroll().line, 91);
  assert.equal(page.lastScroll().flag, false);
});

test('clearing search after a reload retains suppression of restore echoes', () => {
  const page = preview({ blocks: chapterBlocks });
  page.api.restore(720, 0);
  page.api.find('', false, false, true);
  page.emit('scroll');
  page.flush();
  assert.equal(page.lastScroll().flag, true);
  assert.equal(page.window.scrollY, 720);
});

test('hidden/folded blocks do not distort mapping, and nested duplicate lines use their deepest block', () => {
  const page = preview({ blocks: [
    { line: 1, top: 20, bottom: 1100 },
    { line: 1, top: 40, bottom: 200, parentIndex: 0 },
    { line: 11, top: 400, bottom: 500, hidden: true },
    { line: 21, top: 0, bottom: 0, folded: true },
    { line: 41, top: 840, bottom: 1100 }
  ] });
  page.api.syncScroll(21, .5);
  assert.equal(page.window.scrollY, 440);
  assert.equal(page.lastScroll().sourcePosition, 21);
});

test('raw HTML without source blocks falls back to progress in either direction', () => {
  const page = preview();
  page.api.syncScroll(20, .4);
  assert.equal(page.window.scrollY, 800);
  assert.equal(page.lastScroll().sourcePosition, 41);
  assert.equal(page.lastScroll().scrollProgress, .4);
  page.emit('wheel');
  page.scroll(1200);
  assert.equal(page.lastScroll().sourcePosition, 61);
  assert.equal(page.lastScroll().flag, false);
});

test('one long wrapped source line retains a fractional final-line position', () => {
  const page = preview({ blocks: [{ line: 1, top: 40, bottom: 2200 }], lineCount: 1 });
  page.api.syncScroll(1.5, .5);
  assert.equal(page.window.scrollY, 1120);
  assert.equal(page.lastScroll().sourcePosition, 1.5);
});

test('short/empty documents and CRLF or CR line endings return finite source positions', () => {
  for (const markdown of ['', 'one\r\ntwo', 'one\rtwo']) {
    const page = preview({ markdown, documentHeight: 100, height: 200 });
    page.api.syncScroll(2.5, .5);
    assert.equal(page.window.scrollY, 0);
    assert.equal(page.lastScroll().sourcePosition, 1);
    assert.equal(page.lastScroll().scrollProgress, 0);
  }
  for (const markdown of ['one\r\ntwo', 'one\rtwo']) {
    const page = preview({ markdown });
    page.scroll(1000);
    assert.equal(page.lastScroll().sourcePosition, 2);
  }
});
