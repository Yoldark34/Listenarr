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
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { vi, describe, it, expect, beforeEach } from 'vitest'

const apiMocks = vi.hoisted(() => ({
  getAudibleMetadata: vi.fn(),
  previewLibraryPath: vi.fn(),
  getApplicationSettings: vi.fn(),
  getQualityProfiles: vi.fn(),
  getRootFolders: vi.fn(),
  addToLibrary: vi.fn(),
}))

vi.mock('@/services/api', () => ({
  apiService: apiMocks,
}))

import AddLibraryModal from '@/components/domain/audiobook/AddLibraryModal.vue'

// A book only sold on audible.com, seen from an instance configured for France
const bookFromFiles = {
  title: 'A Princess of Mars',
  authors: [],
  narrators: [],
  asin: 'B008DFUGCQ',
  source: 'Audible',
}

const usRecord = {
  metadata: {
    asin: 'B008DFUGCQ',
    title: 'A Princess of Mars',
    authors: [{ name: 'Edgar Rice Burroughs' }],
    narrators: [{ name: 'Bob Thomley' }],
    region: 'us',
  },
  source: 'Audible',
}

// What Audible returns for an ASIN it doesn't sell in that market
const emptyFrenchStub = {
  metadata: { asin: 'B008DFUGCQ', authors: [], narrators: [], region: 'fr' },
  source: 'Audible',
}

const notFound = Object.assign(new Error('No metadata found for ASIN: B008DFUGCQ'), { status: 404 })

function mountModal() {
  return mount(AddLibraryModal, {
    props: { visible: true, book: bookFromFiles },
    attachTo: document.body,
    global: { plugins: [createPinia()] },
  })
}

describe('AddLibraryModal when the configured region has no record for the ASIN', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    apiMocks.getApplicationSettings.mockResolvedValue({
      outputPath: '/library',
      defaultSearchRegion: 'fr',
    })
    apiMocks.getQualityProfiles.mockResolvedValue([])
    apiMocks.getRootFolders.mockResolvedValue([])
    apiMocks.previewLibraryPath.mockResolvedValue({
      fullPath: '/library/Edgar Rice Burroughs/A Princess of Mars',
      relativePath: 'Edgar Rice Burroughs/A Princess of Mars',
    })
    apiMocks.addToLibrary.mockResolvedValue({ message: 'Added', audiobook: { id: 1 } })
  })

  it.each([
    [
      'an empty product stub',
      () => apiMocks.getAudibleMetadata.mockResolvedValueOnce(emptyFrenchStub),
    ],
    ['a 404', () => apiMocks.getAudibleMetadata.mockRejectedValueOnce(notFound)],
  ])('falls back to the US catalogue and keeps that region (%s)', async (_, arrangeFrench) => {
    arrangeFrench()
    apiMocks.getAudibleMetadata.mockResolvedValueOnce(usRecord)
    const wrapper = mountModal()
    await flushPromises()

    expect(apiMocks.getAudibleMetadata).toHaveBeenNthCalledWith(1, 'B008DFUGCQ', 'fr')
    expect(apiMocks.getAudibleMetadata).toHaveBeenNthCalledWith(2, 'B008DFUGCQ', 'us')
    expect(wrapper.find('[data-testid="metadata-not-found"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Edgar Rice Burroughs')

    await wrapper.get('button.btn-primary').trigger('click')
    await flushPromises()
    expect(apiMocks.addToLibrary).toHaveBeenCalledWith(
      expect.objectContaining({ authors: ['Edgar Rice Burroughs'], region: 'us' }),
      expect.any(Object),
    )
  })

  it.each([
    ['empty stubs', () => apiMocks.getAudibleMetadata.mockResolvedValue(emptyFrenchStub)],
    ['404s', () => apiMocks.getAudibleMetadata.mockRejectedValue(notFound)],
  ])('refuses to add the book when neither catalogue has it (%s)', async (_, arrange) => {
    arrange()
    const wrapper = mountModal()
    await flushPromises()

    expect(apiMocks.getAudibleMetadata).toHaveBeenCalledTimes(2)
    const warning = wrapper.get('[data-testid="metadata-not-found"]').text()
    expect(warning).toContain('France (FR)')
    expect(warning).toContain('United States (US)')
    const add = wrapper.get('button.btn-primary')
    expect(add.attributes('disabled')).toBeDefined()

    await add.trigger('click')
    await flushPromises()
    expect(apiMocks.addToLibrary).not.toHaveBeenCalled()
  })

  it('keeps the configured region and does not ask the US catalogue when it has the book', async () => {
    apiMocks.getAudibleMetadata.mockResolvedValue({
      ...usRecord,
      metadata: { ...usRecord.metadata, region: 'fr' },
    })
    const wrapper = mountModal()
    await flushPromises()

    expect(apiMocks.getAudibleMetadata).toHaveBeenCalledTimes(1)
    expect(wrapper.find('[data-testid="metadata-not-found"]').exists()).toBe(false)
    expect(wrapper.get('button.btn-primary').attributes('disabled')).toBeUndefined()
  })

  it('does not retry the same catalogue when the configured region is already US', async () => {
    apiMocks.getApplicationSettings.mockResolvedValue({
      outputPath: '/library',
      defaultSearchRegion: 'us',
    })
    apiMocks.getAudibleMetadata.mockRejectedValue(notFound)
    const wrapper = mountModal()
    await flushPromises()

    expect(apiMocks.getAudibleMetadata).toHaveBeenCalledTimes(1)
    expect(wrapper.get('[data-testid="metadata-not-found"]').text()).toContain('United States (US)')
  })
})
