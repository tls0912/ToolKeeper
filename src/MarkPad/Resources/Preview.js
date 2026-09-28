(() => {
  'use strict';
  const config = window.markpadConfig;
  delete window.markpadConfig;
  const article = document.getElementById('document');
  const language = config.language.startsWith('zh') ? 'zh' : config.language.startsWith('ja') ? 'ja' : 'en';
  const labels = {
    en: { copy:'Copy', copied:'Copied', copyMarkdown:'Copy as Markdown', selectAll:'Select All', open:'Open Link', copyLink:'Copy Link', edit:'Edit Here', fold:'Fold section', close:'Close image' },
    zh: { copy:'複製', copied:'已複製', copyMarkdown:'複製 Markdown', selectAll:'全選', open:'開啟連結', copyLink:'複製連結', edit:'在此編輯', fold:'折疊段落', close:'關閉圖片' },
    ja: { copy:'コピー', copied:'コピーしました', copyMarkdown:'Markdown としてコピー', selectAll:'すべて選択', open:'リンクを開く', copyLink:'リンクをコピー', edit:'ここを編集', fold:'セクションを折りたたむ', close:'画像を閉じる' }
  }[language];
  const send = (type, details = {}) => window.chrome?.webview?.postMessage({ token:config.token, type, ...details });
  const sourceElement = node => (node?.nodeType === Node.ELEMENT_NODE ? node : node?.parentElement)?.closest('[data-source-line]');
  const sourceLine = node => Math.max(1, Number(sourceElement(node)?.dataset.sourceLine) || 1);
  const selectedText = () => window.getSelection()?.toString() || '';
  const copy = text => send('copy', { text });
  function asMarkdown(node) {
    if(node.nodeType===Node.TEXT_NODE)return node.textContent;
    if(node.nodeType!==Node.ELEMENT_NODE&&node.nodeType!==Node.DOCUMENT_FRAGMENT_NODE)return '';
    if(node.nodeType===Node.ELEMENT_NODE&&node.matches('button,.heading-anchor,.code-tools'))return '';
    const inner=[...node.childNodes].map(asMarkdown).join('');
    if(!node.tagName)return inner;
    if(node.classList.contains('code-line'))return inner+'\n';
    switch(node.tagName) {
      case 'STRONG':case 'B':return '**'+inner+'**';
      case 'EM':case 'I':return '*'+inner+'*';
      case 'DEL':case 'S':return '~~'+inner+'~~';
      case 'BR':return '  \n';
      case 'HR':return '\n\n---\n\n';
      case 'IMG':return '!['+(node.getAttribute('alt')||'')+']('+(node.getAttribute('data-original-source')||node.getAttribute('src')||'')+')';
      case 'A':return node.hasAttribute('href')?'['+inner+']('+node.getAttribute('href')+')':inner;
      case 'INPUT':return '['+(node.checked?'x':' ')+'] ';
      case 'CODE': {
        if(node.parentElement?.tagName==='PRE')return inner;
        const ticks='`'.repeat(Math.max(1,...[...inner.matchAll(/`+/g)].map(m=>m[0].length+1)));
        return ticks+(/^[` ]|[` ]$/.test(inner)?' '+inner+' ':inner)+ticks;
      }
      case 'PRE':return '\n\n```'+([...node.querySelector('code')?.classList||[]].find(c=>c.startsWith('language-'))?.slice(9)||'')+'\n'+inner.trimEnd()+'\n```\n\n';
      case 'BLOCKQUOTE':return '\n\n'+inner.trim().split('\n').map(line=>'> '+line).join('\n')+'\n\n';
      case 'LI':return '- '+inner.trim()+'\n';
      case 'P':case 'UL':case 'OL':return '\n\n'+inner.trim()+'\n\n';
      default:if(/^H[1-6]$/.test(node.tagName))return '\n\n'+'#'.repeat(Number(node.tagName[1]))+' '+inner.trim()+'\n\n';return inner;
    }
  }
  const copyMarkdown = () => {
    const selection = window.getSelection();
    if (!selection?.rangeCount || selection.isCollapsed) return;
    const range = selection.getRangeAt(0);
    const first = sourceElement(range.startContainer);
    const last = sourceElement(range.endContainer);
    const fragment=range.cloneContents();
    const complete=first===last&&first&&selectedText().trim()===first.textContent.trim();
    if (complete) {
      const start = Math.max(0, Number(first.dataset.sourceStart) || 0);
      const end = Math.min(config.markdown.length, (Number(last.dataset.sourceEnd) || start) + 1);
      send('copy-markdown', { text:config.markdown.slice(start, Math.max(start, end)) });
    } else {
      let value=asMarkdown(fragment);
      let ancestor=range.commonAncestorContainer.nodeType===Node.TEXT_NODE?range.commonAncestorContainer.parentElement:range.commonAncestorContainer;
      while(ancestor&&ancestor!==article&&!ancestor.matches('p,li,pre,h1,h2,h3,h4,h5,h6,td,th,blockquote,div')) {
        if(ancestor.matches('strong,b'))value='**'+value+'**';else if(ancestor.matches('em,i'))value='*'+value+'*';else if(ancestor.matches('del,s'))value='~~'+value+'~~';else if(ancestor.matches('code'))value='`'+value+'`';
        ancestor=ancestor.parentElement;
      }
      send('copy-markdown',{text:value.replace(/\n{3,}/g,'\n\n').trim()});
    }
  };

  // Tokenize known language families without evaluating code or injecting its text as HTML.
  function highlight(code, languageName) {
    const text = code.textContent;
    const lang = languageName.toLowerCase();
    const known = /^(c|csharp|cs|cpp|c\+\+|java|javascript|js|jsx|typescript|ts|tsx|json|python|py|bash|sh|shell|powershell|ps1|sql|css|html|xml|yaml|yml|go|rust|rs|ruby|rb|php|swift|kotlin|kt)$/;
    const keywords = new Set(('abstract as async await base bool boolean break case catch char class const constructor continue decimal default def delete do double elif else enum except export extends false final finally float fn for foreach from function get if implements import in int interface internal is let long namespace new None null object of override package pass private protected public raise readonly record ref return sbyte sealed self set short static string struct super switch this throw true try typeof uint ulong undefined union unsafe ushort using val var virtual void volatile when while with yield SELECT FROM WHERE INSERT INTO UPDATE DELETE JOIN LEFT RIGHT ON GROUP BY ORDER AND OR NOT AS VALUES CREATE TABLE ALTER DROP NULL TRUE FALSE').split(' '));
    const token = /\/\*[\s\S]*?\*\/|\/\/[^\n]*|#[^\n]*|--[^\n]*|@?"(?:\\.|""|[^"\\])*"|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`|\b(?:0x[\da-fA-F]+|\d+(?:\.\d+)?)\b|\b[A-Za-z_$][\w$]*\b/g;
    const fragment = document.createDocumentFragment();
    let last = 0;
    if (!known.test(lang)) { fragment.append(document.createTextNode(text)); }
    else for (const match of text.matchAll(token)) {
      fragment.append(document.createTextNode(text.slice(last, match.index)));
      const value = match[0];
      const kind = /^(\/\/|\/\*|#|--)/.test(value) ? 'comment' : /^[@"'`]/.test(value) ? 'string' : /^\d/.test(value) ? 'number' : keywords.has(value) ? 'keyword' : '';
      if (kind) { const span = document.createElement('span'); span.className = 'syntax-' + kind; span.textContent = value; fragment.append(span); }
      else fragment.append(document.createTextNode(value));
      last = match.index + value.length;
    }
    if (known.test(lang)) fragment.append(document.createTextNode(text.slice(last)));
    code.replaceChildren(fragment);
    if (config.codeLineNumbers) {
      // Split the highlighted DOM at line breaks, preserving spans and exact code text.
      const lines = [document.createElement('span')];
      lines[0].className = 'code-line';
      for (const node of [...code.childNodes]) {
        const segments = node.textContent.split('\n');
        segments.forEach((segment, i) => {
          if (i) { const line = document.createElement('span'); line.className = 'code-line'; lines.push(line); }
          const part = node.nodeType === Node.ELEMENT_NODE ? node.cloneNode(false) : document.createTextNode('');
          part.textContent = segment;
          lines.at(-1).append(part);
        });
      }
      if (lines.length > 1 && !lines.at(-1).textContent) lines.pop();
      code.classList.add('line-numbers');
      code.replaceChildren(...lines);
    }
  }

  article.querySelectorAll('pre').forEach(pre => {
    const code = pre.querySelector('code');
    if (!code) return;
    const original = code.textContent;
    const name = [...code.classList].find(c => c.startsWith('language-'))?.slice(9) || '';
    highlight(code, name);
    const toolbar = document.createElement('div'); toolbar.className = 'code-tools';
    const tag = document.createElement('span'); tag.textContent = name;
    const button = document.createElement('button'); button.type = 'button'; button.textContent = labels.copy;
    button.addEventListener('click', () => { copy(original); button.textContent = labels.copied; setTimeout(() => button.textContent = labels.copy, 1500); });
    toolbar.append(tag, button); pre.prepend(toolbar);
  });
  article.querySelectorAll('table').forEach(table => {
    const wrapper = document.createElement('div'); wrapper.className = 'table-scroll';
    table.replaceWith(wrapper); wrapper.append(table);
  });

  const folded = new Set();
  const headings = [...article.querySelectorAll('h1,h2,h3,h4,h5,h6')];
  function applyFolds() {
    article.querySelectorAll('.fold-hidden').forEach(el => el.classList.remove('fold-hidden'));
    for (const heading of folded) {
      let next = heading.nextElementSibling;
      while (next && (!/^H[1-6]$/.test(next.tagName) || Number(next.tagName[1]) > Number(heading.tagName[1]))) {
        next.classList.add('fold-hidden'); next = next.nextElementSibling;
      }
    }
  }
  headings.forEach((heading, index) => {
    if (!heading.id) heading.id = 'heading-' + index;
    const anchor = document.createElement('a'); anchor.href = '#' + encodeURIComponent(heading.id); anchor.textContent = '#'; anchor.className = 'heading-anchor'; anchor.setAttribute('aria-label', labels.copyLink);
    const fold = document.createElement('button'); fold.type = 'button'; fold.className = 'heading-fold'; fold.textContent = '▾'; fold.title = labels.fold; fold.setAttribute('aria-expanded','true');
    fold.addEventListener('click', () => {
      if (folded.has(heading)) folded.delete(heading); else folded.add(heading);
      fold.textContent = folded.has(heading) ? '▸' : '▾'; fold.setAttribute('aria-expanded', String(!folded.has(heading))); applyFolds(); scheduleScrollReport();
    });
    heading.prepend(fold); heading.append(anchor);
  });

  let overlay = null;
  function closeOverlay() {
    if (!overlay) return false;
    overlay.remove(); overlay = null; send('overlay', { flag:false }); return true;
  }
  function showImage(source) {
    closeOverlay();
    overlay = document.createElement('div'); overlay.className = 'image-overlay'; overlay.setAttribute('role','dialog'); overlay.setAttribute('aria-modal','true'); overlay.setAttribute('aria-label', source.alt || labels.close);
    const picture = document.createElement('img'); picture.src = source.src; picture.alt = source.alt; picture.draggable = false;
    const close = document.createElement('button'); close.type = 'button'; close.textContent = '×'; close.setAttribute('aria-label', labels.close); close.addEventListener('click', closeOverlay);
    overlay.append(picture, close); document.body.append(overlay); close.focus(); send('overlay', { flag:true });
    let scale = 1, x = 0, y = 0, drag = null;
    const transform = () => {
      const maxX = Math.max(0, (picture.offsetWidth * scale - innerWidth) / 2);
      const maxY = Math.max(0, (picture.offsetHeight * scale - innerHeight) / 2);
      x = Math.max(-maxX, Math.min(maxX, x)); y = Math.max(-maxY, Math.min(maxY, y));
      picture.style.transform = `translate(${x}px,${y}px) scale(${scale})`;
      picture.style.cursor = maxX || maxY ? 'grab' : 'default';
    };
    overlay.addEventListener('click', event => { if (event.target === overlay) closeOverlay(); });
    overlay.addEventListener('wheel', event => {
      if (!event.ctrlKey) return;
      event.preventDefault(); scale = Math.max(.2, Math.min(8, scale * (event.deltaY < 0 ? 1.12 : 1 / 1.12))); transform();
    }, { passive:false });
    picture.addEventListener('pointerdown', event => {
      if (picture.offsetWidth * scale <= innerWidth && picture.offsetHeight * scale <= innerHeight) return;
      drag = { id:event.pointerId, x:event.clientX - x, y:event.clientY - y }; picture.setPointerCapture(event.pointerId); event.preventDefault();
    });
    picture.addEventListener('pointermove', event => { if (drag) { x = event.clientX - drag.x; y = event.clientY - drag.y; transform(); } });
    picture.addEventListener('pointerup', () => drag = null);
    picture.addEventListener('pointercancel', () => drag = null);
  }

  // A short final chapter may be unable to reach the viewport top. Keep the chosen
  // chapter active through the anchor animation and its stationary final position.
  // The next user scroll or a separate programmatic scroll releases that choice.
  let anchorTarget=null, anchorScrolling=false, anchorSettleTimer=0;
  function clearAnchorTarget() {
    anchorTarget=null;anchorScrolling=false;clearTimeout(anchorSettleTimer);
  }
  function settleAnchorScroll() {
    anchorScrolling=false;clearTimeout(anchorSettleTimer);
  }
  function awaitAnchorScroll() {
    clearTimeout(anchorSettleTimer);
    anchorSettleTimer=setTimeout(settleAnchorScroll,150);
  }
  window.addEventListener('scrollend',settleAnchorScroll,{passive:true});
  window.addEventListener('wheel',clearAnchorTarget,{passive:true});
  window.addEventListener('pointerdown',clearAnchorTarget,{passive:true});
  window.addEventListener('keydown',event=>{
    if(['ArrowUp','ArrowDown','PageUp','PageDown','Home','End',' '].includes(event.key))clearAnchorTarget();
  });

  function openLink(href) {
    if (href.startsWith('#')) {
      let id; try { id = decodeURIComponent(href.slice(1)); } catch { return; }
      const target = document.getElementById(id);
      if (target && article.contains(target)) {
        releaseScrollSync();
        // Reveal only sections containing the target, preserving unrelated folded sections.
        for (const heading of [...folded]) {
          let next = heading.nextElementSibling;
          while (next && (!/^H[1-6]$/.test(next.tagName) || Number(next.tagName[1]) > Number(heading.tagName[1]))) {
            if (next === target || next.contains(target)) { folded.delete(heading); break; }
            next = next.nextElementSibling;
          }
        }
        applyFolds();
        headings.forEach(heading => { const button=heading.querySelector('.heading-fold');if(button){button.textContent=folded.has(heading)?'▸':'▾';button.setAttribute('aria-expanded',String(!folded.has(heading)));} });
        for (let ancestor=target.parentElement;ancestor&&ancestor!==article;ancestor=ancestor.parentElement)
          if (ancestor.tagName === 'DETAILS') ancestor.open = true;
        anchorTarget=target;anchorScrolling=true;awaitAnchorScroll();
        target.scrollIntoView({ behavior:'smooth', block:'start' });
        // Even an anchor already in view (or at the document bottom) needs to notify the
        // native outline; the anchor remains selected if scrolling is bottom-clamped.
        reportScroll(target);
      }
    } else send('link', { text:href });
  }
  article.addEventListener('click', event => {
    const image = event.target.closest('img'); if (image) { event.preventDefault(); showImage(image); return; }
    const link = event.target.closest('a[href]'); if (link) { event.preventDefault(); openLink(link.getAttribute('href')); }
  });
  article.addEventListener('change', event => {
    const checkbox = event.target.closest('input[data-task-line]');
    if (checkbox && !config.readOnly) send('task', { line:Number(checkbox.dataset.taskLine), flag:checkbox.checked });
  });

  let menu = null;
  const closeMenu = () => { menu?.remove(); menu = null; };
  document.addEventListener('contextmenu', event => {
    event.preventDefault(); closeMenu();
    if (overlay) return;
    const target = event.target;
    const text = selectedText();
    const link = target.closest('a[href]');
    menu = document.createElement('div'); menu.className = 'preview-menu'; menu.setAttribute('role','menu');
    const item = (label, action, enabled=true) => {
      const button=document.createElement('button'); button.type='button'; button.textContent=label; button.disabled=!enabled; button.setAttribute('role','menuitem');
      button.addEventListener('mousedown', e=>e.preventDefault());
      button.addEventListener('click', () => { action(); closeMenu(); }); menu.append(button);
    };
    item(labels.copy, () => copy(text), !!text);
    item(labels.copyMarkdown, copyMarkdown, !!text);
    item(labels.selectAll, () => { const range=document.createRange(); range.selectNodeContents(article); const selection=window.getSelection(); selection.removeAllRanges(); selection.addRange(range); });
    if (link) { item(labels.open,()=>openLink(link.getAttribute('href'))); item(labels.copyLink,()=>send('copy-link',{text:link.getAttribute('href')})); }
    item(labels.edit,()=>send('edit',{line:sourceLine(target)}));
    document.body.append(menu);
    menu.style.left=Math.max(4,Math.min(event.clientX,innerWidth-menu.offsetWidth-4))+'px';
    menu.style.top=Math.max(4,Math.min(event.clientY,innerHeight-menu.offsetHeight-4))+'px';
  });
  document.addEventListener('pointerdown', event => { if (menu && !menu.contains(event.target)) closeMenu(); });
  document.addEventListener('copy', event => {
    if (!selectedText()) return;
    event.preventDefault(); event.clipboardData.setData('text/plain', selectedText());
  });
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape') { if (menu) {closeMenu();event.preventDefault();return;} if(closeOverlay()){event.preventDefault();return;} }
    if (event.ctrlKey && event.shiftKey && event.key.toLowerCase() === 'c') {event.preventDefault();copyMarkdown();return;}
    const key = event.key.length === 1 ? event.key.toUpperCase() : event.key;
    const shortcut = (event.ctrlKey ? 'Ctrl+' : '') + (event.shiftKey ? 'Shift+' : '') + key;
    if (['Ctrl+F','Ctrl+S','Ctrl+Shift+S','Ctrl+E','Ctrl+W','Ctrl+N','Ctrl+O','Ctrl+Tab','Ctrl+Shift+Tab','F11','F3','Shift+F3','Escape'].includes(shortcut)) {
      event.preventDefault(); send('shortcut',{text:shortcut});
    }
  });

  let searchQuery='', searchCase=false, searchGroups=[], searchIndex=-1;
  function clearSearch() {
    article.querySelectorAll('mark.mp-search').forEach(mark=>mark.replaceWith(document.createTextNode(mark.textContent)));
    article.normalize(); searchGroups=[];searchIndex=-1;
  }
  function collectSearchNodes() {
    const nodes=[];let full='';let block=null;
    const walker=document.createTreeWalker(article,NodeFilter.SHOW_TEXT,{acceptNode:node =>
      node.parentElement.closest('button,.code-tools,.heading-anchor,.fold-hidden') ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT });
    let node;
    while ((node=walker.nextNode())) {
      const current=node.parentElement.closest('.code-line,p,li,h1,h2,h3,h4,h5,h6,pre,td,th,blockquote');
      if (block && current !== block) full+='\n';
      block=current;nodes.push({node,start:full.length,end:full.length+node.textContent.length});full+=node.textContent;
    }
    return {nodes,full};
  }
  function find(text, matchCase, backwards=false, restart=false) {
    clearAnchorTarget();
    let wrapped=false;
    if (text !== searchQuery || matchCase !== searchCase || restart) {
      clearSearch();searchQuery=text;searchCase=matchCase;
      if (text) {
        const {nodes,full}=collectSearchNodes();
        // Escape every metacharacter so search remains literal; case-insensitive regex keeps source
        // offsets correct for Unicode characters whose lowercase spelling has a different length.
        const expression=new RegExp(text.replace(/[.*+?^${}()|[\]\\]/g,'\\$&'),matchCase?'g':'gi');
        const hits=[];let match;
        while ((match=expression.exec(full))!==null && hits.length<20000) hits.push({start:match.index,end:match.index+match[0].length});
        searchGroups=hits.map(()=>[]);
        let cursor=hits.length-1;
        for(let n=nodes.length-1;n>=0;n--) {
          const entry=nodes[n];const relevant=[];
          while(cursor>=0&&hits[cursor].start>=entry.end)cursor--;
          for(let index=cursor;index>=0&&hits[index].end>entry.start;index--) {
            const hit=hits[index];if(hit.start<entry.end)relevant.push({start:Math.max(0,hit.start-entry.start),end:Math.min(entry.end-entry.start,hit.end-entry.start),index});
          }
          for(const hit of relevant) {
            const range=document.createRange();range.setStart(entry.node,hit.start);range.setEnd(entry.node,hit.end);
            const mark=document.createElement('mark');mark.className='mp-search';range.surroundContents(mark);searchGroups[hit.index].unshift(mark);
          }
        }
        const firstVisible=searchGroups.findIndex(group=>group[0]?.getBoundingClientRect().bottom>=0);
        searchIndex=searchGroups.length?(firstVisible<0?0:firstVisible):-1;
        if(backwards && searchGroups.length) searchIndex=Math.max(0,searchIndex-1);
      }
    } else if(searchGroups.length) {
      const next=searchIndex+(backwards?-1:1);wrapped=next<0||next>=searchGroups.length;searchIndex=(next+searchGroups.length)%searchGroups.length;
    }
    article.querySelectorAll('mark.mp-search.active').forEach(mark=>mark.classList.remove('active'));
    if(searchIndex>=0) {releaseScrollSync();searchGroups[searchIndex].forEach(mark=>mark.classList.add('active'));searchGroups[searchIndex][0]?.scrollIntoView({block:'center',behavior:'instant'});}
    send('search',{count:searchGroups.length,index:searchIndex+1,flag:wrapped});
  }
  // Source positions are fractional lines so long paragraphs/code blocks can move
  // continuously between their rendered block boundaries. Count source lines once.
  const sourceLineCount=config.markdown.split(/\r\n|\r|\n/).length;
  const sourceBlocks=[...article.querySelectorAll('[data-source-line]')];
  const clamp=(value,min,max)=>Math.max(min,Math.min(max,value));
  const scrollLimit=()=>Math.max(0,document.documentElement.scrollHeight-innerHeight);
  function sourceGeometry() {
    const blocks=sourceBlocks.filter(element=>element.getClientRects().length&&!element.closest('.fold-hidden'))
      .map(element=>({element,line:clamp(Number(element.dataset.sourceLine)||1,1,sourceLineCount),bounds:element.getBoundingClientRect()}));
    const points=[];
    for(const block of blocks) {
      const point={line:block.line,top:block.bounds.top+window.scrollY,element:block.element};
      const previous=points.at(-1);
      // A list item/paragraph may share its parent's source line. Prefer the
      // deepest block, but ignore unrelated duplicate lines (notably raw HTML).
      if(previous&&point.line===previous.line) {
        if(previous.element.contains(point.element)&&point.top>=previous.top)points[points.length-1]=point;
      } else if(!previous||point.line>previous.line&&point.top>previous.top)points.push(point);
    }
    if(points.length&&points[0].line>1)points.unshift({line:1,top:0});
    if(points.length) {
      const bottom=Math.max(points.at(-1).top,article.getBoundingClientRect().bottom+window.scrollY);
      points.push({line:sourceLineCount+1,top:bottom});
    }
    return {blocks,points};
  }
  function interpolate(points,value,from,to) {
    if(value<=points[0][from])return points[0][to];
    for(let index=1;index<points.length;index++) {
      const start=points[index-1],end=points[index];
      if(value<=end[from]) {
        const distance=end[from]-start[from];
        return distance>0?start[to]+(end[to]-start[to])*(value-start[from])/distance:end[to];
      }
    }
    return points.at(-1)[to];
  }
  // Instant scrolling updates scrollY synchronously; Chromium may report the event
  // later. Keep that stationary echo suppressed until the viewport actually moves,
  // including native scrollbar movement that does not produce DOM pointer events.
  let synchronizedScrollY=null;
  function releaseScrollSync() {synchronizedScrollY=null;}
  function updateScrollOwner() {
    if(synchronizedScrollY!==null&&Math.abs(window.scrollY-synchronizedScrollY)>.5)releaseScrollSync();
  }
  function synchronizedScroll(action) {
    clearAnchorTarget();
    synchronizedScrollY=window.scrollY;
    action();
    synchronizedScrollY=window.scrollY;
    reportScroll();
  }
  function syncScroll(line,progress) {
    const maximum=scrollLimit();
    const fraction=Number.isFinite(progress)?clamp(progress,0,1):null;
    let top;
    if(fraction===0||fraction===1)top=maximum*fraction;
    else {
      const {points}=sourceGeometry();
      top=points.length&&Number.isFinite(line)?interpolate(points,clamp(line,1,sourceLineCount+1),'line','top'):maximum*(fraction||0);
    }
    synchronizedScroll(()=>window.scrollTo({top:clamp(top,0,maximum),behavior:'instant'}));
  }
  let scrollScheduled=false;
  function reportScroll(preferredTarget=null) {
    updateScrollOwner();
    const {blocks,points}=sourceGeometry();
    let visible=preferredTarget||(anchorTarget?.getClientRects().length?anchorTarget:null);
    if (!visible) {
      // Prefer the deepest visible source block, not a list/table container spanning pages.
      const crossing=blocks.filter(block=>block.bounds.top<=32&&block.bounds.bottom>8);
      visible=(crossing.at(-1)||blocks.find(block=>block.bounds.bottom>=8)||blocks.at(-1))?.element;
      if (window.scrollY>0&&window.scrollY+innerHeight>=document.documentElement.scrollHeight-2)
        visible=[...headings].reverse().find(heading=>heading.getClientRects().length&&!heading.closest('.fold-hidden'))||visible;
    }
    const maximum=scrollLimit();
    // Chromium can stop at fractional CSS pixels while scrollHeight is rounded.
    const progress=maximum>0&&window.scrollY>0?(maximum-window.scrollY<=1?1:clamp(window.scrollY/maximum,0,1)):0;
    const position=points.length?interpolate(points,window.scrollY,'top','line'):1+progress*sourceLineCount;
    send('scroll',{text:String(window.scrollY),line:sourceLine(visible),sourcePosition:clamp(position,1,sourceLineCount+.999999),scrollProgress:progress,flag:synchronizedScrollY!==null});
  }
  function scheduleScrollReport() {
    if(scrollScheduled)return;scrollScheduled=true;
    requestAnimationFrame(()=>{scrollScheduled=false;reportScroll();});
  }
  window.addEventListener('scroll',()=>{
    closeMenu();
    if(anchorTarget){if(anchorScrolling)awaitAnchorScroll();else clearAnchorTarget();}
    scheduleScrollReport();
  },{passive:true});
  window.markpad={find,syncScroll,selection:selectedText,scroll:()=>window.scrollY,closeOverlay:()=>{if(menu){closeMenu();return true;}return closeOverlay();},
    restore:(scroll,line)=>synchronizedScroll(()=>{const target=line?sourceBlocks.reduce((best,el)=>Number(el.dataset.sourceLine)<=line?el:best,null):null;if(target)target.scrollIntoView({block:'start',behavior:'instant'});else window.scrollTo({top:Math.max(0,scroll||0),behavior:'instant'});}),
    theme:(dark,ink=false)=>{const root=document.documentElement;root.classList.toggle('dark',dark);root.classList.toggle('light',!dark);root.classList.toggle('ink',ink);root.dataset.theme=ink?(dark?'ink-dark':'ink'):dark?'dark':'light';},
    anchor:openLink};
  send('ready');
})();
