/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
// Start Matching's automatic pick (issue #1237): a search result is only taken as the match when its
// title (and author, when both sides have one) resembles what the scan detected. The search answers a
// pool of loose candidates; when none of them is the book, the row must stay unmatched for the user
// to pick, instead of taking the first candidate.
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { SearchResult } from '@/types'

const advancedSearch = vi.fn()
const getApplicationSettings = vi.fn()

vi.mock('@/services/api', () => ({
  apiService: {
    advancedSearch,
    getApplicationSettings,
    getSavedUnmatchedFiles: vi.fn(),
  },
}))

vi.mock('@/services/signalr', () => ({
  signalRService: { onUnmatchedScanComplete: vi.fn(() => () => {}) },
}))

vi.mock('@/utils/logger', () => ({ logger: { debug: vi.fn() } }))

// A scanned row as an `Author/Series/NN - Title` folder with correct embedded tags gives it
function scannedBook(folder: string, title: string, author: string) {
  const fullPath = `/audiobooks/${author}/${folder}/01.mp3`
  return {
    id: fullPath,
    fullPath,
    sourceFiles: [fullPath],
    folderPath: `/audiobooks/${author}/${folder}`,
    relativePath: `${author}/${folder}`,
    folderName: folder,
    detectedTitle: title,
    detectedAuthor: author,
    format: 'MP3',
    fileCount: 1,
    selectedMatch: null,
    hasSearched: false,
    isSearching: false,
    selected: false,
  }
}

function result(asin: string, title: string, author?: string): SearchResult {
  return {
    asin,
    title,
    // Title search results often carry authors without names (only the full record has them)
    authors: author ? [{ name: author }] : [{ asin: 'B000AUTHOR' }],
  } as SearchResult
}

async function matchOne(book: ReturnType<typeof scannedBook>, candidates: SearchResult[]) {
  const { useLibraryImportStore } = await import('@/stores/libraryImport')
  const store = useLibraryImportStore()
  advancedSearch.mockResolvedValue(candidates)
  store.items = { [book.id]: book }

  store.startProcessing()
  await vi.waitFor(() => expect(store.isProcessing).toBe(false))

  const row = store.items[book.id]!
  expect(row.hasSearched).toBe(true)
  return row
}

describe('library import automatic matching (issue #1237)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    setActivePinia(createPinia())
    getApplicationSettings.mockResolvedValue({ defaultSearchRegion: 'us' })
  })

  it('leaves a book unmatched when no candidate shares its title or author', async () => {
    const row = await matchOne(
      scannedBook('03 - A Storm of Swords', 'A Storm of Swords', 'George R.R. Martin'),
      [result('B00TEST001', 'American Film', 'American Film Institute')],
    )

    expect(row.selectedMatch).toBeNull()
    expect(row.selected).toBe(false)
  })

  it('leaves a book unmatched when the only candidate has an unrelated title and no author name', async () => {
    const row = await matchOne(
      scannedBook('01 - The Hunger Games', 'The Hunger Games', 'Suzanne Collins'),
      [result('B00TEST002', 'Fiction 100')],
    )

    expect(row.selectedMatch).toBeNull()
    expect(row.selected).toBe(false)
  })

  it('does not fall back to the first candidate when none is by the detected author', async () => {
    const row = await matchOne(
      scannedBook('02 - Looking Good Dead', 'Looking Good Dead', 'Peter James'),
      [
        result('B00TEST003', 'Adventures of Huckleberry Finn', 'Mark Twain'),
        result('B00TEST004', 'Tom Sawyer', 'Mark Twain'),
      ],
    )

    expect(row.selectedMatch).toBeNull()
  })

  it('rejects a candidate that only shares one word of the title', async () => {
    const row = await matchOne(
      scannedBook('14 - The Overlook', 'The Overlook', 'Michael Connelly'),
      [result('B00TEST005', 'Title I overlook', 'Some Educator')],
    )

    expect(row.selectedMatch).toBeNull()
  })

  it('prefers the candidate with the detected title over another book by the same author', async () => {
    const row = await matchOne(scannedBook('01 - Slow Horses', 'Slow Horses', 'Mick Herron'), [
      result('B00TEST006', 'Slough House', 'Mick Herron'),
      result('B00TEST007', 'Slow Horses', 'Mick Herron'),
    ])

    expect(row.selectedMatch?.asin).toBe('B00TEST007')
  })

  it('does not take another book of the same series by the same author', async () => {
    const row = await matchOne(scannedBook('01 - Slow Horses', 'Slow Horses', 'Mick Herron'), [
      result('B00TEST006', 'Slough House', 'Mick Herron'),
    ])

    expect(row.selectedMatch).toBeNull()
  })

  // Guards for the fix: a close title is still matched automatically
  it('still matches a candidate whose title adds a series suffix to the detected one', async () => {
    const row = await matchOne(scannedBook('01 - Slow Horses', 'Slow Horses', 'Mick Herron'), [
      result('B00TEST007', 'Slow Horses: Slough House, Book 1', 'Mick Herron'),
    ])

    expect(row.selectedMatch?.asin).toBe('B00TEST007')
    expect(row.selected).toBe(true)
  })

  it('still matches the same title when the author is written differently', async () => {
    const row = await matchOne(
      scannedBook('03 - A Storm of Swords', 'A Storm of Swords', 'George R.R. Martin'),
      [result('B00TEST008', 'A Storm of Swords', 'George R. R. Martin')],
    )

    expect(row.selectedMatch?.asin).toBe('B00TEST008')
  })
})
