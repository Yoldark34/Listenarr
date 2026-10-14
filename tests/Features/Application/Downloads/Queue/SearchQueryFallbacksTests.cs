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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Downloads.Queue
{
    [Trait("Area", "Search")]
    [Trait("Name", "SearchQueryFallbacksTests")]
    [Trait("Category", "Search")]
    public class SearchQueryFallbacksTests : BaseTests
    {
        [Fact]
        public void Expand_KeepsCombinedQueryFirst()
        {
            var queries = SearchQueryFallbacks.Expand("Red Rising Pierce Brown", "Red Rising");

            Assert.Equal("Red Rising Pierce Brown", queries[0]);
        }

        [Fact]
        public void Expand_FallsBackToTitleOnly()
        {
            var queries = SearchQueryFallbacks.Expand("Project Hail Mary Andy Weir", "Project Hail Mary");

            Assert.Contains("Project Hail Mary", queries);
        }

        [Fact]
        public void Expand_FallsBackToTitleWithoutEditionSuffix()
        {
            var queries = SearchQueryFallbacks.Expand(
                "Red Rising (Part 1 of 2) (Dramatized Adaptation) Pierce Brown",
                "Red Rising (Part 1 of 2) (Dramatized Adaptation)");

            Assert.Equal("Red Rising", queries[^1]);
        }

        [Fact]
        public void Expand_DoesNotRepeatIdenticalQueries()
        {
            var queries = SearchQueryFallbacks.Expand("Dune", "Dune");

            Assert.Single(queries);
        }

        [Fact]
        public void Expand_IgnoresMissingTitle()
        {
            var queries = SearchQueryFallbacks.Expand("Dune Frank Herbert", null);

            Assert.Equal(new[] { "Dune Frank Herbert" }, queries);
        }

        [Fact]
        public async Task SearchAsync_StillRunsThePrimarySearchWhenTheQueryIsEmpty()
        {
            var search = new Mock<ISearchService>();
            search.Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<List<string>?>(),
                    It.IsAny<SearchSortBy>(), It.IsAny<SearchSortDirection>(), true))
                .ReturnsAsync([]);

            var (query, results) = await SearchQueryFallbacks.SearchAsync(search.Object, string.Empty, null);

            Assert.Equal(string.Empty, query);
            Assert.Empty(results);
            search.Verify(s => s.SearchAsync(string.Empty, null, It.IsAny<List<string>?>(),
                It.IsAny<SearchSortBy>(), It.IsAny<SearchSortDirection>(), true), Times.Once);
        }

        [Fact]
        public async Task SearchAsync_RetriesWithTitleInAudiobookCategoryAndStopsAtFirstResults()
        {
            // An author credited as "Stephen King (alias Richard Bachman)" makes the combined query find nothing.
            var search = new Mock<ISearchService>();
            search.Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<List<string>?>(),
                    It.IsAny<SearchSortBy>(), It.IsAny<SearchSortDirection>(), true))
                .ReturnsAsync((string query, string? _, List<string>? _, SearchSortBy _, SearchSortDirection _, bool _) =>
                    query == "Marche ou crève" ? [new SearchResult { Title = "Marche.ou.creve.Stephen.King.FR" }] : []);

            var (query, results) = await SearchQueryFallbacks.SearchAsync(
                search.Object, "Marche ou crève Stephen King (alias Richard Bachman)", "Marche ou crève");

            Assert.Equal("Marche ou crève", query);
            Assert.Single(results);
            search.Verify(s => s.SearchAsync("Marche ou crève Stephen King (alias Richard Bachman)", null, It.IsAny<List<string>?>(),
                It.IsAny<SearchSortBy>(), It.IsAny<SearchSortDirection>(), true), Times.Once);
            search.Verify(s => s.SearchAsync("Marche ou crève", SearchQueryFallbacks.RetryCategory, It.IsAny<List<string>?>(),
                It.IsAny<SearchSortBy>(), It.IsAny<SearchSortDirection>(), true), Times.Once);
            search.VerifyNoOtherCalls();
        }
    }
}
