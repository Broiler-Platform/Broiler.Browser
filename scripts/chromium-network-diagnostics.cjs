// An isolated, bounded Chromium control for Broiler network investigations.
// Uses an existing Playwright installation; does not install or alter browsers.
const fs = require('node:fs');
const path = require('node:path');

function safeUrl(value) {
  try {
    const url = new URL(value);
    url.username = ''; url.password = ''; url.hash = '';
    for (const key of new Set(url.searchParams.keys())) {
      if (!(key === 'q' && url.searchParams.getAll(key).length === 1 && url.searchParams.get(key) === 'test'))
        url.searchParams.set(key, '[redacted]');
    }
    return url.href;
  } catch { return '[non-URL]'; }
}

function headers(values) {
  return Object.fromEntries(Object.entries(values).map(([name, value]) => {
    const lower = name.toLowerCase();
    if (lower === 'cookie') return [name, value.split(';').map(x => x.trim().split('=')[0])];
    if (lower === 'set-cookie') return [name, value.split('\n').map(x => x.split('=')[0])];
    if (lower === 'referer' || lower === 'location') return [name, safeUrl(value)];
    const keep = /^(user-agent|accept|accept-language|accept-encoding|sec-ch-ua.*|sec-fetch-.*|content-type|content-length|cache-control|pragma|server|retry-after|upgrade-insecure-requests|:method|:scheme|:authority)$/;
    return [name, keep.test(lower) ? value : '[redacted]'];
  }));
}

async function record(context, page, outputDir) {
  fs.mkdirSync(outputDir, { recursive: true });
  const journal = path.join(outputDir, 'events.jsonl');
  // Never append a new session to an earlier run.
  fs.writeFileSync(journal, '', { flag: 'wx' });
  const pending = new Set();
  const responses = [];
  let bodyId = 0;
  let lastDocumentActivity = Date.now();
  const emit = (event, data) => fs.appendFileSync(journal,
    JSON.stringify({ time: new Date().toISOString(), event, ...data }) + '\n');
  const track = task => {
    pending.add(task);
    task.catch(error => emit('diagnostic-error', { error: String(error) }))
      .finally(() => pending.delete(task));
  };
  const cdp = await context.newCDPSession(page);
  cdp.on('Network.requestWillBeSent', e => {
    if (e.type === 'Document') lastDocumentActivity = Date.now();
    emit('request', {
    id: e.requestId, type: e.type, method: e.request.method, url: safeUrl(e.request.url),
    timestamp: e.timestamp, headers: headers(e.request.headers),
    redirect: e.redirectResponse && { status: e.redirectResponse.status,
      url: safeUrl(e.redirectResponse.url), protocol: e.redirectResponse.protocol },
    });
  });
  cdp.on('Network.requestWillBeSentExtraInfo', e => emit('request-extra', {
    id: e.requestId, headers: headers(e.headers),
    cookies: e.associatedCookies.map(x => ({ name: x.cookie.name, domain: x.cookie.domain,
      path: x.cookie.path, blockedReasons: x.blockedReasons })),
  }));
  cdp.on('Network.responseReceivedExtraInfo', e => emit('response-extra', {
    id: e.requestId, status: e.statusCode, headers: headers(e.headers),
    blockedCookies: e.blockedCookies.map(x => ({ name: x.cookie?.name,
      blockedReasons: x.blockedReasons })),
  }));
  cdp.on('Network.responseReceived', e => emit('response', {
    id: e.requestId, type: e.type, url: safeUrl(e.response.url), status: e.response.status,
    protocol: e.response.protocol, remoteIPAddress: e.response.remoteIPAddress,
    remotePort: e.response.remotePort, connectionId: e.response.connectionId,
    connectionReused: e.response.connectionReused, timing: e.response.timing,
    security: e.response.securityDetails && {
      protocol: e.response.securityDetails.protocol, cipher: e.response.securityDetails.cipher,
      keyExchangeGroup: e.response.securityDetails.keyExchangeGroup,
      issuer: e.response.securityDetails.issuer,
    },
  }));
  cdp.on('Network.loadingFailed', e => emit('loading-failed', {
    id: e.requestId, type: e.type, error: e.errorText, canceled: e.canceled,
    blockedReason: e.blockedReason, corsErrorStatus: e.corsErrorStatus,
  }));
  await cdp.send('Network.enable');
  page.on('pageerror', error => emit('page-error', { error: String(error), stack: error.stack }));
  page.on('crash', () => emit('crash', {}));
  page.on('console', message => {
    if (['warning', 'error'].includes(message.type()))
      emit('console', { level: message.type(), text: message.text() });
  });
  page.on('response', response => {
    if (response.request().resourceType() !== 'document') return;
    const row = { url: safeUrl(response.url()), status: response.status(),
      mainFrame: response.frame() === page.mainFrame(), method: response.request().method() };
    responses.push(row);
    if (response.status() >= 200 && !(response.status() >= 300 && response.status() < 400)) {
      const filename = `document-${++bodyId}-${response.status()}.html`;
      track(response.body().then(body => {
        fs.writeFileSync(path.join(outputDir, filename), body);
        row.body = filename; row.bytes = body.length;
      }).catch(error => {
        row.bodyError = String(error);
        emit('body-error', { url: row.url, status: row.status, error: row.bodyError });
      }));
    }
  });
  return {
    emit,
    async settle() {
      // A load event alone is not terminal: challenge scripts may navigate afterwards.
      const deadline = Date.now() + 10000;
      while (Date.now() < deadline && Date.now() - lastDocumentActivity < 1000)
        await new Promise(resolve => setTimeout(resolve, 100));
      emit('settle', { quiet: Date.now() - lastDocumentActivity >= 1000,
        limitMs: 10000, quietMs: 1000 });
    },
    async snapshot(stage) {
      const state = await page.evaluate(() => ({
        title: document.title, text: document.body?.innerText ?? '',
        buttons: [...document.querySelectorAll('button')].map(x => x.innerText),
        navigator: { userAgent: navigator.userAgent, webdriver: navigator.webdriver,
          language: navigator.language, languages: navigator.languages, platform: navigator.platform },
      }));
      fs.writeFileSync(path.join(outputDir, stage + '.json'), JSON.stringify({
        ...state, url: safeUrl(page.url()),
        cookies: (await context.cookies()).map(({ value, ...metadata }) => metadata),
      }, null, 2));
      await page.screenshot({ path: path.join(outputDir, stage + '.png') });
      return { url: safeUrl(page.url()),
        title: /^https?:/.test(state.title) ? safeUrl(state.title) : state.title,
        buttons: state.buttons, navigator: state.navigator,
        lastMainDocument: responses.filter(x => x.mainFrame).at(-1) };
    },
    async finish() {
      // Closing this test page cancels never-ending response bodies and prevents new navigations.
      let timer;
      try {
        await Promise.race([
          page.close().then(() => Promise.allSettled([...pending])),
          new Promise(resolve => { timer = setTimeout(resolve, 5000); }),
        ]);
      } finally { clearTimeout(timer); }
      emit('finish', { pendingBodies: pending.size, pageClosed: page.isClosed() });
      fs.writeFileSync(path.join(outputDir, 'documents.json'), JSON.stringify(responses, null, 2));
      return responses;
    },
  };
}

module.exports = { record, safeUrl, headers };

if (require.main === module) (async () => {
  const { parseArgs } = require('node:util');
  const { values } = parseArgs({ options: {
    url: { type: 'string' }, output: { type: 'string' }, playwright: { type: 'string' },
    executable: { type: 'string' }, 'consent-button': { type: 'string' },
  } });
  if (!values.url || !values.output || !values.playwright || !values.executable)
    throw new Error('Required: --url URL --output NEW_DIRECTORY --playwright MODULE_PATH --executable CHROME_PATH. Optional: --consent-button EXACT_VISIBLE_LABEL.');
  const output = path.resolve(values.output);
  // Reserve a new evidence directory before browser startup (NetLog writes immediately).
  fs.mkdirSync(path.dirname(output), { recursive: true });
  fs.mkdirSync(output);
  const { chromium } = require(path.resolve(values.playwright));
  const browser = await chromium.launch({ executablePath: values.executable, headless: true,
    args: ['--log-net-log=' + path.join(output, 'netlog.json')] });
  let capture;
  try {
    const context = await browser.newContext({ viewport: { width: 1365, height: 900 } });
    const page = await context.newPage();
    page.setDefaultTimeout(15000);
    capture = await record(context, page, output);
    capture.emit('run', { browser: browser.version(), headless: true, url: safeUrl(values.url) });
    const initialResponse = await page.goto(values.url, { waitUntil: 'load', timeout: 60000 });
    capture.emit('initial-goto', { status: initialResponse?.status(),
      url: safeUrl(initialResponse?.url() ?? '') });
    async function snapshot(stage) {
      await capture.settle();
      try { return await capture.snapshot(stage); }
      catch (error) {
        if (!/Execution context was destroyed|because of a navigation/.test(String(error))) throw error;
        await capture.settle();
        return capture.snapshot(stage);
      }
    }
    console.log(JSON.stringify(await snapshot('initial'), null, 2));
    if (values['consent-button']) {
      // The caller must inspect the consent page and supply its actual visible label.
      await page.getByRole('button', { name: values['consent-button'], exact: true }).click();
      await page.waitForLoadState('load', { timeout: 60000 });
      console.log(JSON.stringify(await snapshot('after-consent'), null, 2));
    }
  } finally {
    try { if (capture) await capture.finish(); }
    finally { await browser.close(); }
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
