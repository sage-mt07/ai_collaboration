const { chromium } = require('playwright');
const fs = require('fs');

(async () => {
  const browser = await chromium.launchPersistentContext('./claude-session', {
    headless: false,
    viewport: { width: 1280, height: 800 }
  });

  const page = await browser.newPage();
  await page.goto('https://claude.ai/recents');

  // スクロールし続けてすべて読み込む
  let prevHeight = 0;
  while (true) {
    await page.mouse.wheel(0, 2000);
    await page.waitForTimeout(1000);
    const currHeight = await page.evaluate(() => document.body.scrollHeight);
    if (currHeight === prevHeight) break;
    prevHeight = currHeight;
  }

  // UUID抽出
  const urls = await page.$$eval('a[href*="/chat/"]', as =>
    as.map(a => a.href).filter((v, i, a) => a.indexOf(v) === i)
  );

  fs.writeFileSync('claude_urls.txt', urls.join('\n'), 'utf-8');
  console.log(`✅ ${urls.length}件のURLを取得しました`);

  // await browser.close(); ← 必要なら閉じてOK
})();
