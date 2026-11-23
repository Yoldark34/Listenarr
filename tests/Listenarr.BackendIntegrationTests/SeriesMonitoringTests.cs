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
using Xunit;

namespace Listenarr.BackendIntegrationTests;

/// <summary>
/// Monitoring a series adds its missing books to the library. The fake Audible serves the "Barsoom"
/// series (tests/Listenarr.FakeProviders/Fixtures/Audible/search-barsoom.json, B0BARSOOMS.json) with
/// one book, B0GODSMARS.
/// </summary>
[Trait("Name", "SeriesMonitoringTests")]
[Trait("Category", "BackendIntegration")]
public sealed class SeriesMonitoringTests : BackendIntegrationTests
{
    private const string SeriesName = "Barsoom";
    private const string BookAsin = "B0GODSMARS";

    [Fact]
    public async Task MonitorSeries_AddsItsBooksWithTheRegionOfTheMonitor()
    {
        var monitored = await Host.SendJsonAsync(HttpMethod.Post, "/api/v1/series/monitoring", new
        {
            name = SeriesName,
            region = "us",
            language = "english",
        });
        Assert.Equal(1, (int)monitored["addedCount"]!);

        // The book was found in the US catalogue: its ASIN keeps that region, or the library lists it
        // as broken (missing region) and Rescan metadata no longer knows where to look it up.
        var book = (await Host.GetLibraryAsync()).Single(item => (string?)item!["asin"] == BookAsin)!;
        Assert.Equal("us", (string?)book["asinRegion"]);
        Assert.Empty(book["brokenReasons"]!.AsArray());
    }
}
