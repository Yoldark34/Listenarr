import { expect, libraryAudiobookFixture, test, LIBRARY_PATH } from '../support/test.js'

// Library > Filters > "Monitored Only" (issue #1238): "the UI hangs".

const book = (id: number, monitored: boolean) =>
  libraryAudiobookFixture({
    id,
    title: `${monitored ? 'Monitored' : 'Unmonitored'} book ${String(id).padStart(4, '0')}`,
    authors: [`Author ${id % 50}`],
    monitored,
  })

async function chooseFilter(page: import('@playwright/test').Page, label: string) {
  await page.locator('.filters-dropdown .trigger').click()
  await page
    .locator('.filters-dropdown .dropdown-item')
    .filter({ has: page.getByText(label, { exact: true }) })
    .click()
}

test.describe('Library Monitored filter (API-stubbed)', () => {
  test.beforeEach(async ({ page }) => {
    await page.setViewportSize({ width: 1600, height: 900 })
  })

  test('shows the monitored books after scrolling down a large library', async ({ api, page }) => {
    // 600 books, only the last 30 monitored: the filtered list is shorter than the scrolled offset
    await api.stubLibrary(Array.from({ length: 600 }, (_, i) => book(i + 1, i >= 570)))
    await page.goto(LIBRARY_PATH)
    await expect(page.locator('.audiobook-poster-container').first()).toBeVisible()

    await page.locator('.audiobooks-scroll-container').evaluate((el) => {
      el.scrollTop = el.scrollHeight / 2
    })
    await page.waitForTimeout(300)

    await chooseFilter(page, 'Monitored Only')

    await expect(page.locator('.audiobook-poster-container')).not.toHaveCount(0, { timeout: 3000 })
    await expect(page.getByText('Monitored book 0571')).toBeVisible({ timeout: 3000 })
  })

  test('shows the books again after resetting a Monitored filter that matched none', async ({
    api,
    page,
  }) => {
    await api.stubLibrary(Array.from({ length: 200 }, (_, i) => book(i + 1, false)))
    await page.goto(LIBRARY_PATH)
    await expect(page.locator('.audiobook-poster-container').first()).toBeVisible()

    await chooseFilter(page, 'Monitored Only')
    await page.waitForTimeout(300)
    await chooseFilter(page, 'Monitored Only') // selecting it again clears it

    await expect(page.locator('.audiobook-poster-container')).not.toHaveCount(0, { timeout: 3000 })
  })

  test('stays responsive when filtering a large library', async ({ api, page }) => {
    await api.stubLibrary(Array.from({ length: 3000 }, (_, i) => book(i + 1, i % 2 === 0)))
    await page.goto(LIBRARY_PATH)
    await expect(page.locator('.audiobook-poster-container').first()).toBeVisible()

    const started = Date.now()
    await chooseFilter(page, 'Monitored Only')
    await expect(page.getByText('Monitored book 0001')).toBeVisible({ timeout: 5000 })
    expect(Date.now() - started).toBeLessThan(2000)
  })
})
