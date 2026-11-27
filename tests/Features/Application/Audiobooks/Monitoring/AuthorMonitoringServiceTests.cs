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
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Application.Audiobooks.Monitoring
{
    public class AuthorMonitoringServiceTests
    {
        [Fact]
        public async Task MonitorAuthorAsync_PersistsAuthorAndAddsOnlyMissingBooksForSelectedLanguage()
        {
            var dbOptions = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(databaseName: $"author-monitor-{System.Guid.NewGuid():N}")
                .Options;

            await using var dbContext = new ListenArrDbContext(dbOptions);
            dbContext.Audiobooks.Add(new Audiobook
            {
                Id = 10,
                Title = "Project Hail Mary",
                Authors = new List<string> { "Andy Weir" },
                Language = "english",
                Monitored = true
            });
            dbContext.QualityProfiles.Add(new QualityProfile { Id = 1, Name = "Standard", IsDefault = true });
            await dbContext.SaveChangesAsync();

            var authorCatalogService = new Mock<IAuthorCatalogService>();
            authorCatalogService
                .Setup(service => service.GetCatalogAsync("Andy Weir", "uk", 500, null, false, It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new AuthorCatalogFetchResult
                {
                    Author = new AuthorLookupItem
                    {
                        Asin = "AUTHOR123",
                        Name = "Andy Weir"
                    },
                    Books = new List<AudibleSearchResult>
                    {
                        new()
                        {
                            Title = "Project Hail Mary",
                            Authors = new List<AudibleAuthor> { new() { Name = "Andy Weir" } },
                            Language = "en-us"
                        },
                        new()
                        {
                            Asin = "B000MARTIAN",
                            Title = "The Martian",
                            Authors = new List<AudibleAuthor> { new() { Name = "Andy Weir" } },
                            Language = "english"
                        },
                        new()
                        {
                            Asin = "B000GERMAN",
                            Title = "Der Marsianer",
                            Authors = new List<AudibleAuthor> { new() { Name = "Andy Weir" } },
                            Language = "de"
                        }
                    }
                });

            var libraryAddService = new Mock<ILibraryAddService>();
            libraryAddService
                .Setup(service => service.AddToLibraryAsync(
                    It.Is<LibraryAddOperationRequest>(request =>
                        request.Metadata.Title == "The Martian" &&
                        request.Monitored &&
                        request.QualityProfileId == 1 &&
                        request.HistorySource == "AuthorMonitoring"),
                    It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new LibraryAddOperationResult
                {
                    Added = true,
                    Message = "Audiobook added to library successfully",
                    Audiobook = new Audiobook
                    {
                        Id = 11,
                        Title = "The Martian",
                        Authors = new List<string> { "Andy Weir" },
                        Asin = "B000MARTIAN",
                        Monitored = true
                    }
                });

            var authorsRepo = new EfMonitoredAuthorRepository(dbContext);
            var audiobooksRepo = new AudiobookRepository(dbContext);

            var service = new AuthorMonitoringService(
                authorsRepo,
                audiobooksRepo,
                authorCatalogService.Object,
                libraryAddService.Object,
                CreateQualityProfileService(),
                Mock.Of<ILogger<AuthorMonitoringService>>());

            var result = await service.MonitorAuthorAsync(new MonitorAuthorRequest
            {
                Name = "Andy Weir",
                Region = "uk",
                Language = "english"
            });

            Assert.NotNull(result.MonitoredAuthor);
            Assert.True(result.SyncResult.Succeeded);
            Assert.Equal(1, result.SyncResult.AddedCount);
            Assert.Equal(1, result.SyncResult.ExistingCount);
            Assert.Equal(0, result.SyncResult.FailedCount);
            Assert.Equal("AUTHOR123", result.MonitoredAuthor!.AuthorAsin);
            Assert.Equal("uk", result.MonitoredAuthor.Region);
            Assert.Equal("english", result.MonitoredAuthor.Language);
            Assert.NotNull(result.MonitoredAuthor.LastSuccessfulSyncAt);
            Assert.Equal(1, result.MonitoredAuthor.QualityProfileId);

            libraryAddService.Verify(service => service.AddToLibraryAsync(
                It.IsAny<LibraryAddOperationRequest>(),
                It.IsAny<System.Threading.CancellationToken>()),
                Times.Once);

            var storedAuthor = await dbContext.MonitoredAuthors.SingleAsync();
            Assert.Equal("Andy Weir", storedAuthor.AuthorName);
            Assert.Equal("andy weir", storedAuthor.AuthorNameNormalized);
            Assert.Equal("AUTHOR123", storedAuthor.AuthorAsin);
        }

        [Fact]
        public async Task SyncDueAuthorsAsync_ForceRefreshesPersistedCatalog()
        {
            var dbOptions = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(databaseName: $"author-monitor-refresh-{System.Guid.NewGuid():N}")
                .Options;

            await using var dbContext = new ListenArrDbContext(dbOptions);
            dbContext.MonitoredAuthors.Add(new MonitoredAuthor
            {
                Id = 1,
                AuthorName = "Andy Weir",
                AuthorNameNormalized = "andy weir",
                AuthorAsin = "AUTHOR123",
                Region = "us",
                Language = "english",
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                UpdatedAt = DateTime.UtcNow.AddDays(-2),
                LastCheckedAt = DateTime.UtcNow.AddDays(-2)
            });
            await dbContext.SaveChangesAsync();

            var authorCatalogService = new Mock<IAuthorCatalogService>();
            authorCatalogService
                .Setup(service => service.GetCatalogAsync("Andy Weir", "us", 500, null, true, It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new AuthorCatalogFetchResult
                {
                    Author = new AuthorLookupItem
                    {
                        Asin = "AUTHOR123",
                        Name = "Andy Weir"
                    },
                    Books = new List<AudibleSearchResult>()
                });

            var authorsRepo = new EfMonitoredAuthorRepository(dbContext);
            var audiobooksRepo = new AudiobookRepository(dbContext);

            var service = new AuthorMonitoringService(
                authorsRepo,
                audiobooksRepo,
                authorCatalogService.Object,
                Mock.Of<ILibraryAddService>(),
                CreateQualityProfileService(),
                Mock.Of<ILogger<AuthorMonitoringService>>());

            var syncedCount = await service.SyncDueAuthorsAsync();

            Assert.Equal(1, syncedCount);
            authorCatalogService.Verify(
                catalog => catalog.GetCatalogAsync("Andy Weir", "us", 500, null, true, It.IsAny<System.Threading.CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task MonitorAuthorAsync_MapsEverySeriesEntryToAMembershipWithItsAsin()
        {
            var metadata = await CaptureAddedBookMetadataAsync(new AudibleSearchResultBuilder()
                .WithAsin("B00CQ5WAXW")
                .WithTitle("She And Allan")
                .WithAuthor("H. Rider Haggard")
                .WithLanguage("english")
                .WithSeries("Ayesha", "0", "B01E633FQM")
                .WithSeries("Allan Quatermain", null, "B01F5TL5K4")
                .Build());

            Assert.NotNull(metadata.SeriesMemberships);
            Assert.Collection(
                metadata.SeriesMemberships!,
                first =>
                {
                    Assert.Equal("Ayesha", first.SeriesName);
                    Assert.Equal("0", first.SeriesNumber);
                    Assert.Equal("B01E633FQM", first.SeriesAsin);
                    Assert.True(first.IsPrimary);
                    Assert.Equal(0, first.SortOrder);
                },
                second =>
                {
                    Assert.Equal("Allan Quatermain", second.SeriesName);
                    Assert.Null(second.SeriesNumber);
                    Assert.Equal("B01F5TL5K4", second.SeriesAsin);
                    Assert.False(second.IsPrimary);
                    Assert.Equal(1, second.SortOrder);
                });

            Assert.Equal("Ayesha", metadata.Series);
            Assert.Equal("0", metadata.SeriesNumber);
        }

        [Fact]
        public async Task MonitorAuthorAsync_LeavesSeriesFieldsUnsetWhenCatalogBookHasNoSeries()
        {
            var metadata = await CaptureAddedBookMetadataAsync(new AudibleSearchResultBuilder()
                .WithAsin("B004YWTD30")
                .WithTitle("She: A History of Adventure")
                .WithAuthor("H. Rider Haggard")
                .WithLanguage("english")
                .Build());

            Assert.Null(metadata.SeriesMemberships);
            Assert.Null(metadata.Series);
            Assert.Null(metadata.SeriesNumber);
        }

        [Fact]
        public async Task MonitorAuthorAsync_KeepsSeriesMembershipWhenCatalogEntryHasNoAsin()
        {
            var metadata = await CaptureAddedBookMetadataAsync(new AudibleSearchResultBuilder()
                .WithAsin("B004YWTD30")
                .WithTitle("She: A History of Adventure")
                .WithAuthor("H. Rider Haggard")
                .WithLanguage("english")
                .WithSeries("Ayesha", "1", "   ")
                .Build());

            var membership = Assert.Single(metadata.SeriesMemberships!);
            Assert.Equal("Ayesha", membership.SeriesName);
            Assert.Equal("1", membership.SeriesNumber);
            Assert.Null(membership.SeriesAsin);
            Assert.True(membership.IsPrimary);
            Assert.Equal("Ayesha", metadata.Series);
            Assert.Equal("1", metadata.SeriesNumber);
        }

        private static async Task<AudibleBookMetadata> CaptureAddedBookMetadataAsync(AudibleSearchResult catalogBook)
        {
            var dbOptions = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(databaseName: $"author-monitor-series-{System.Guid.NewGuid():N}")
                .Options;

            await using var dbContext = new ListenArrDbContext(dbOptions);
            dbContext.QualityProfiles.Add(new QualityProfile { Id = 1, Name = "Standard", IsDefault = true });
            await dbContext.SaveChangesAsync();

            var authorCatalogService = new Mock<IAuthorCatalogService>();
            authorCatalogService
                .Setup(service => service.GetCatalogAsync("H. Rider Haggard", "us", 500, null, false, It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new AuthorCatalogFetchResult
                {
                    Author = new AuthorLookupItem
                    {
                        Asin = "AUTHOR456",
                        Name = "H. Rider Haggard"
                    },
                    Books = new List<AudibleSearchResult> { catalogBook }
                });

            AudibleBookMetadata? addedMetadata = null;
            var libraryAddService = new Mock<ILibraryAddService>();
            libraryAddService
                .Setup(service => service.AddToLibraryAsync(
                    It.IsAny<LibraryAddOperationRequest>(),
                    It.IsAny<System.Threading.CancellationToken>()))
                .Callback<LibraryAddOperationRequest, System.Threading.CancellationToken>(
                    (request, _) => addedMetadata = request.Metadata)
                .ReturnsAsync(new LibraryAddOperationResult
                {
                    Added = true,
                    Message = "Audiobook added to library successfully",
                    Audiobook = new Audiobook
                    {
                        Id = 11,
                        Title = catalogBook.Title,
                        Authors = new List<string> { "H. Rider Haggard" },
                        Asin = catalogBook.Asin,
                        Monitored = true
                    }
                });

            var service = new AuthorMonitoringService(
                new EfMonitoredAuthorRepository(dbContext),
                new AudiobookRepository(dbContext),
                authorCatalogService.Object,
                libraryAddService.Object,
                CreateQualityProfileService(),
                Mock.Of<ILogger<AuthorMonitoringService>>());

            var result = await service.MonitorAuthorAsync(new MonitorAuthorRequest
            {
                Name = "H. Rider Haggard",
                Region = "us",
                Language = "english"
            });

            Assert.Equal(1, result.SyncResult.AddedCount);
            Assert.NotNull(addedMetadata);
            return addedMetadata!;
        }

        [Fact]
        public async Task SyncAuthorAsync_QualityProfileRejection_ReportsFailure()
        {
            var dbOptions = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(databaseName: $"author-monitor-profile-{System.Guid.NewGuid():N}")
                .Options;

            await using var dbContext = new ListenArrDbContext(dbOptions);
            dbContext.MonitoredAuthors.Add(new MonitoredAuthor
            {
                Id = 1,
                AuthorName = "Andy Weir",
                AuthorNameNormalized = "andy weir",
                Region = "us",
                Language = "english"
            });
            await dbContext.SaveChangesAsync();

            var authorCatalogService = new Mock<IAuthorCatalogService>();
            authorCatalogService
                .Setup(service => service.GetCatalogAsync("Andy Weir", "us", 500, null, true, It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new AuthorCatalogFetchResult
                {
                    Author = new AuthorLookupItem { Name = "Andy Weir" },
                    Books = new List<AudibleSearchResult>
                    {
                        new() { Asin = "B000MARTIAN", Title = "The Martian", Language = "english" },
                        new() { Asin = "B000ARTEMIS", Title = "Artemis", Language = "english" }
                    }
                });

            const string rejection = "A quality profile is required. Select a quality profile, or create one in Settings > Quality Profiles.";
            var libraryAddService = new Mock<ILibraryAddService>();
            libraryAddService
                .Setup(service => service.AddToLibraryAsync(It.IsAny<LibraryAddOperationRequest>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new LibraryAddOperationResult
                {
                    ValidationFailed = true,
                    ValidationCode = LibraryAddValidationCodes.QualityProfileRequired,
                    ValidationField = "qualityProfileId",
                    ValidationMessage = rejection,
                    Message = rejection
                });

            var service = new AuthorMonitoringService(
                new EfMonitoredAuthorRepository(dbContext),
                new AudiobookRepository(dbContext),
                authorCatalogService.Object,
                libraryAddService.Object,
                CreateQualityProfileService(),
                Mock.Of<ILogger<AuthorMonitoringService>>());

            var result = await service.SyncAuthorAsync(1);

            Assert.False(result.Succeeded);
            Assert.Equal(0, result.AddedCount);
            Assert.Equal(1, result.FailedCount);
            Assert.Equal(rejection, result.ErrorMessage);
            var storedAuthor = await dbContext.MonitoredAuthors.SingleAsync();
            Assert.Null(storedAuthor.LastSuccessfulSyncAt);
            Assert.Equal(rejection, storedAuthor.LastError);
            libraryAddService.Verify(add => add.AddToLibraryAsync(
                    It.IsAny<LibraryAddOperationRequest>(),
                    It.IsAny<System.Threading.CancellationToken>()),
                Times.Once);
        }

        // Issue #1235: a monitored author's sync adds its whole back catalogue as monitored,
        // re-adds books the library already holds, and adds every edition of the same book.
        // Titles, ASINs and release dates are Audible UK's (api.audible.co.uk, Nov 2026).

        [Fact]
        public async Task SyncAuthorAsync_AddsTheWholeBackCatalogueAsMonitoredBooks()
        {
            // What the issue reports, pinned: every catalogue book, however old, is added as a
            // monitored (wanted) book, so the automatic search picks each one up. Nothing in
            // MonitorAuthorRequest lets the user add them unmonitored or keep future releases only.
            var sync = await SyncMonitoredAuthorAsync(
                library: new List<Audiobook>(),
                catalog: new List<AudibleSearchResult>
                {
                    RowlingBook("B017V568SY", "Harry Potter and the Philosopher's Stone, Book 1", "2015-11-20"),
                    RowlingBook("B017V4IM1G", "Harry Potter and the Chamber of Secrets, Book 2", "2015-11-20"),
                    RowlingBook("B0F14RFHS6", "Harry Potter and the Philosopher’s Stone (Full-Cast Edition)", "2025-11-04")
                });

            Assert.True(sync.Result.Succeeded);
            Assert.Equal(3, sync.Result.AddedCount);
            Assert.All(sync.Requests, request =>
            {
                Assert.True(request.Monitored);
                Assert.Equal(1, request.QualityProfileId);
            });
        }

        [Fact]
        public async Task SyncAuthorAsync_SkipsAnOwnedBookWithTheSameAsinInAnotherRootFolder()
        {
            var sync = await SyncMonitoredAuthorAsync(
                library: new List<Audiobook>
                {
                    OwnedBook("Harry Potter and the Philosopher's Stone, Book 1", "J.K. Rowling", asin: "B017V568SY")
                },
                catalog: new List<AudibleSearchResult>
                {
                    RowlingBook("B017V568SY", "Harry Potter and the Philosopher's Stone, Book 1", "2015-11-20")
                });

            Assert.Equal(0, sync.Result.AddedCount);
            Assert.Equal(1, sync.Result.ExistingCount);
            Assert.Empty(sync.Requests);
        }

        [Fact]
        public async Task SyncAuthorAsync_DoesNotReAddAnOwnedBookWhoseTitleHasNoBookNumberSuffix()
        {
            // An owned book imported from disk without an ASIN carries the title of its files or
            // folder; Audible's catalogue title adds ", Book 1".
            var sync = await SyncMonitoredAuthorAsync(
                library: new List<Audiobook>
                {
                    OwnedBook("Harry Potter and the Philosopher's Stone", "J.K. Rowling", asin: null)
                },
                catalog: new List<AudibleSearchResult>
                {
                    RowlingBook("B017V568SY", "Harry Potter and the Philosopher's Stone, Book 1", "2015-11-20")
                });

            Assert.Equal(1, sync.Result.ExistingCount);
            Assert.Equal(0, sync.Result.AddedCount);
            Assert.Empty(sync.Requests);
        }

        [Fact]
        public async Task SyncAuthorAsync_DoesNotReAddAnOwnedBookWhoseAuthorInitialsAreSpaced()
        {
            // "J. K. Rowling" (tags, OpenLibrary) and "J.K. Rowling" (Audible) are the same author.
            var sync = await SyncMonitoredAuthorAsync(
                library: new List<Audiobook>
                {
                    OwnedBook("Harry Potter and the Philosopher's Stone, Book 1", "J. K. Rowling", asin: null)
                },
                catalog: new List<AudibleSearchResult>
                {
                    RowlingBook("B017V568SY", "Harry Potter and the Philosopher's Stone, Book 1", "2015-11-20")
                });

            Assert.Equal(1, sync.Result.ExistingCount);
            Assert.Equal(0, sync.Result.AddedCount);
            Assert.Empty(sync.Requests);
        }

        [Fact]
        public async Task SyncAuthorAsync_AddsOnlyOneEditionOfTheSameBook()
        {
            // Audible UK lists the Stephen Fry and the full-cast recordings as two products.
            var sync = await SyncMonitoredAuthorAsync(
                library: new List<Audiobook>(),
                catalog: new List<AudibleSearchResult>
                {
                    RowlingBook("B017V568SY", "Harry Potter and the Philosopher's Stone, Book 1", "2015-11-20", "Stephen Fry"),
                    RowlingBook("B0F14RFHS6", "Harry Potter and the Philosopher’s Stone (Full-Cast Edition)", "2025-11-04", "Hugh Laurie")
                });

            Assert.Equal(1, sync.Result.AddedCount);
            Assert.Single(sync.Requests);
        }

        private sealed record AuthorSync(MonitorAuthorSyncResult Result, List<LibraryAddOperationRequest> Requests);

        private static AudibleSearchResult RowlingBook(string asin, string title, string releaseDate, string narrator = "Stephen Fry")
        {
            var book = new AudibleSearchResultBuilder()
                .WithAsin(asin)
                .WithTitle(title)
                .WithAuthor("J.K. Rowling")
                .WithLanguage("english")
                .Build();
            book.ReleaseDate = releaseDate;
            book.Narrators = new List<AudibleNarrator> { new() { Name = narrator } };
            return book;
        }

        private static Audiobook OwnedBook(string title, string author, string? asin) => new()
        {
            Title = title,
            Authors = new List<string> { author },
            Asin = asin,
            Language = "english",
            Monitored = false,
            BasePath = "/audiobooks-owned/" + title
        };

        private static async Task<AuthorSync> SyncMonitoredAuthorAsync(List<Audiobook> library, List<AudibleSearchResult> catalog)
        {
            var dbOptions = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(databaseName: $"author-monitor-1235-{System.Guid.NewGuid():N}")
                .Options;

            await using var dbContext = new ListenArrDbContext(dbOptions);
            dbContext.Audiobooks.AddRange(library);
            dbContext.MonitoredAuthors.Add(new MonitoredAuthor
            {
                Id = 1,
                AuthorName = "J.K. Rowling",
                AuthorNameNormalized = "jk rowling",
                Region = "uk",
                Language = "english",
                QualityProfileId = 1
            });
            await dbContext.SaveChangesAsync();

            var authorCatalogService = new Mock<IAuthorCatalogService>();
            authorCatalogService
                .Setup(service => service.GetCatalogAsync("J.K. Rowling", "uk", 500, null, true, It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new AuthorCatalogFetchResult
                {
                    Author = new AuthorLookupItem { Asin = "B000AP9A6K", Name = "J.K. Rowling" },
                    Books = catalog
                });

            var requests = new List<LibraryAddOperationRequest>();
            var libraryAddService = new Mock<ILibraryAddService>();
            libraryAddService
                .Setup(service => service.AddToLibraryAsync(It.IsAny<LibraryAddOperationRequest>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync((LibraryAddOperationRequest request, System.Threading.CancellationToken _) =>
                {
                    requests.Add(request);
                    return new LibraryAddOperationResult
                    {
                        Added = true,
                        Message = "Audiobook added to library successfully",
                        Audiobook = new Audiobook
                        {
                            Id = 100 + requests.Count,
                            Title = request.Metadata.Title,
                            Authors = request.Metadata.Authors,
                            Asin = request.Metadata.Asin,
                            Monitored = request.Monitored
                        }
                    };
                });

            var service = new AuthorMonitoringService(
                new EfMonitoredAuthorRepository(dbContext),
                new AudiobookRepository(dbContext),
                authorCatalogService.Object,
                libraryAddService.Object,
                CreateQualityProfileService(),
                Mock.Of<ILogger<AuthorMonitoringService>>());

            var result = await service.SyncAuthorAsync(1);
            return new AuthorSync(result, requests);
        }

        private static IQualityProfileService CreateQualityProfileService(QualityProfile? defaultProfile = null)
        {
            defaultProfile ??= new QualityProfile { Id = 1, Name = "Standard", IsDefault = true };
            var qualityProfileService = new Mock<IQualityProfileService>();
            qualityProfileService
                .Setup(service => service.GetDefaultAsync())
                .ReturnsAsync(defaultProfile);
            qualityProfileService
                .Setup(service => service.GetByIdAsync(defaultProfile.Id))
                .ReturnsAsync(defaultProfile);
            return qualityProfileService.Object;
        }
    }
}
