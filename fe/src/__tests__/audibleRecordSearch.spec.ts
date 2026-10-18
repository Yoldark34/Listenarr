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
import { beforeEach, describe, expect, it, vi } from 'vitest'

const advancedSearch = vi.hoisted(() => vi.fn())
vi.mock('@/services/api', () => ({ apiService: { advancedSearch } }))

import { reactive } from 'vue'
import {
  createRecordSearchState,
  findRecords,
  lookupAsin,
  onRegionChanged,
  searchRecords,
} from '@/utils/audibleRecordSearch'
import type { SearchResult } from '@/types'

describe('findRecords', () => {
  beforeEach(() => {
    advancedSearch.mockReset()
  })

  // The server drops empty records and tags each result with its region: results are used as is
  it('searches the title and author in one region and returns the server results', async () => {
    advancedSearch.mockResolvedValue([
      { title: 'Primal Hunter 4', asin: 'B0BK9W82PQ', authors: [], region: 'us' },
    ])

    const results = await findRecords({ title: ' The Primal Hunter 4 ', author: 'Zogarth' }, 'us')

    expect(advancedSearch).toHaveBeenCalledWith({
      title: 'The Primal Hunter 4',
      author: 'Zogarth',
      region: 'us',
      cap: 8,
    })
    expect(results).toEqual([expect.objectContaining({ asin: 'B0BK9W82PQ', region: 'us' })])
  })

  it('looks an ASIN up as such, without the author', async () => {
    advancedSearch.mockResolvedValue([])

    await findRecords({ asin: 'b0bk9w82pq', title: 'ignored', author: 'ignored' }, 'fr')

    expect(advancedSearch).toHaveBeenCalledWith({ asin: 'B0BK9W82PQ', region: 'fr', cap: 5 })
  })

  it('searches nothing for an empty query', async () => {
    expect(await findRecords({ title: '  ' }, 'fr')).toEqual([])
    expect(advancedSearch).not.toHaveBeenCalled()
  })
})

describe('one search at a time', () => {
  // Each request waits until the test answers it, keyed by region
  let pending: Record<string, (results: SearchResult[]) => void>
  let signals: Record<string, AbortSignal | undefined>

  beforeEach(() => {
    advancedSearch.mockReset()
    pending = {}
    signals = {}
    advancedSearch.mockImplementation(
      (params: { region: string }, signal?: AbortSignal) =>
        new Promise<SearchResult[]>((resolve) => {
          pending[params.region] = resolve
          signals[params.region] = signal
        }),
    )
  })

  const book = (asin: string, title: string, region: string) =>
    ({ asin, title, authors: [{ name: 'Zogarth' }], region }) as unknown as SearchResult

  it('keeps the results of the region picked last when the earlier search answers after it', async () => {
    const state = reactive(
      createRecordSearchState({ region: 'fr', query: 'Primal Hunter 4', author: 'Zogarth' }),
    )
    const fr = searchRecords(state)
    state.region = 'us'
    const us = onRegionChanged(state)
    expect(signals.fr?.aborted).toBe(true)

    pending.us!([book('B0BK9W82PQ', 'Primal Hunter 4', 'us')])
    await us
    pending.fr!([book('B0HM45F8MW', 'Primal Hunter, Vol. 7', 'fr')])
    await fr

    expect(state.results.map((r) => r.asin)).toEqual(['B0BK9W82PQ'])
    expect(state.searching).toBe(false)
    expect(state.searched).toBe(true)
  })

  it('does not let a title search answering late replace the ASIN looked up since', async () => {
    const state = reactive(createRecordSearchState({ region: 'fr', query: 'Primal Hunter' }))
    const search = searchRecords(state)
    const searchAnswer = pending.fr!
    state.asin = 'B0HM45F8MW'
    const lookup = lookupAsin(state)

    pending.fr!([book('B0HM45F8MW', 'Primal Hunter, Vol. 7', 'fr')])
    await lookup
    searchAnswer([book('B0FXXVV42J', 'Primal Hunter, Tome 4', 'fr')])
    await search

    expect(state.results.map((r) => r.asin)).toEqual(['B0HM45F8MW'])
    expect(state.chosen).toEqual({ asin: 'B0HM45F8MW', region: 'fr' })
  })

  it('does not offer the previous results while a new search runs', async () => {
    const state = reactive(createRecordSearchState({ region: 'fr', query: 'Primal Hunter 4' }))
    const first = searchRecords(state)
    pending.fr!([book('B0HM45F8MW', 'Primal Hunter, Vol. 7', 'fr')])
    await first
    expect(state.results).toHaveLength(1)

    void searchRecords(state)

    expect(state.results).toEqual([])
    expect(state.searching).toBe(true)
  })
})
