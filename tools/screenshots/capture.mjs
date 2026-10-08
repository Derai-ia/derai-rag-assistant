import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { chromium } from 'playwright';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '../..');
const output = path.join(root, 'docs', 'img');
const baseUrl = process.env.DERAI_BASE_URL ?? 'http://localhost:8080';
let api;

async function waitForApi() {
  for (let attempt = 0; attempt < 60; attempt += 1) {
    try {
      const response = await fetch(`${baseUrl}/health/ready`);
      if (response.ok) return;
    } catch { /* API is still starting. */ }
    await delay(1000);
  }
  throw new Error(`API did not become ready at ${baseUrl}`);
}

try {
  await import('node:fs/promises').then(fs => fs.mkdir(output, { recursive: true }));
  try {
    const response = await fetch(`${baseUrl}/health/ready`);
    if (!response.ok) throw new Error('API is not ready');
  } catch {
    api = spawn('dotnet', ['run', '--project', path.join(root, 'src', 'Derai.RagAssistant.Api'), '--configuration', 'Release', '--urls', baseUrl], {
      cwd: root,
      stdio: 'ignore',
      windowsHide: true
    });
    await waitForApi();
  }

  const browser = await chromium.launch({ headless: true });
  try {
    const newPage = async () => {
      const page = await browser.newPage({ viewport: { width: 1440, height: 1120 }, deviceScaleFactor: 1 });
      await page.goto(baseUrl, { waitUntil: 'networkidle' });
      const languageAuth = page.waitForResponse(response => response.url().endsWith('/api/v1/auth/demo') && response.request().method() === 'POST');
      await page.locator('#language').click();
      await languageAuth;
      return page;
    };

    const page = await newPage();
    const directorAuth = page.waitForResponse(response => response.url().endsWith('/api/v1/auth/demo') && response.request().method() === 'POST');
    await page.locator('#role').selectOption('direccion');
    await directorAuth;
    await page.locator('#question').fill('What are the confidential commercial terms and negotiated standard pallet rate?');
    await page.locator('#form button').click();
    await page.locator('#sources .source').first().waitFor();
    await page.locator('#messages .citation').first().waitFor();
    await page.getByText('contrato-marco-transportista.md').waitFor();
    await page.screenshot({ path: path.join(output, 'chat-direccion.png'), fullPage: true });
    await page.locator('#theme').click();
    await page.screenshot({ path: path.join(output, 'chat-direccion-dark.png'), fullPage: true });

    const supportPage = await newPage();
    await supportPage.locator('#question').fill('What are the confidential commercial terms and negotiated standard pallet rate?');
    await supportPage.locator('#form button').click();
    await supportPage.getByText('I could not find enough information').waitFor();
    await supportPage.screenshot({ path: path.join(output, 'chat-soporte.png'), fullPage: true });

    const qualityPage = await newPage();
    await qualityPage.locator('#eval').click();
    await qualityPage.locator('#runEval').click();
    await qualityPage.getByText('Hit-rate@5: 100% (20/20)').waitFor();
    await qualityPage.screenshot({ path: path.join(output, 'calidad.png'), fullPage: true });
    console.log('Captured docs/img/chat-direccion.png, chat-direccion-dark.png, chat-soporte.png, calidad.png');
  } finally {
    await browser.close();
  }
} finally {
  if (api && !api.killed) api.kill();
}
