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
using System.Net;
using System.Text.Json.Nodes;

using Xunit;

namespace Listenarr.BackendIntegrationTests;

/// <summary>
/// Library Import collisions (issue #1236), through the API, with the calls the Library Import page makes
/// (<c>importSelected</c> in <c>fe/src/stores/libraryImport.ts</c>): <c>POST /library/add</c> with
/// <c>destinationPath</c> = the book's folder for "Leave files in place" (the selected root folder otherwise),
/// a 409 answer taken as the existing entry, then <c>POST /library/manual-import</c> with the book's files.
/// </summary>
[Trait("Name", "LibraryImportCollisionTests")]
[Trait("Category", "BackendIntegration")]
public sealed class LibraryImportCollisionTests : BackendIntegrationTests
{
    private static string SilenceFile => Path.Join(AppContext.BaseDirectory, "Data", "the-time-machine-silence.m4b");

    [Fact]
    public async Task LeaveInPlace_AttachesTheFilesToAnExistingFilelessEntryOfAnotherRootFolder()
    {
        // A book already in the library without files (added from an author page), its folder under the library root.
        const string asin = "B0IS123601";
        var existingBasePath = Path.Join(Host.LibraryPath, "Issue Author", "Existing Book");
        var existingId = await AddAsync(asin, "Existing Book", existingBasePath);

        // Its real files sit in the import root.
        var bookFolder = Path.Join(Host.ImportPath, "Issue Author", "Existing Book");
        var source = CopySilence(bookFolder, "Existing Book.m4b");

        // The page adds the match with the book's folder as destination; the 409 carries the existing entry.
        var conflict = await AddResponseAsync(asin, "Existing Book", bookFolder);
        Assert.True(conflict.Status == HttpStatusCode.Conflict, $"Second add: {(int)conflict.Status} {conflict.Body.ToJsonString()}");
        Assert.Equal(existingId, (int)conflict.Body["audiobook"]!["id"]!);

        var import = await ManualImportAsync(bookFolder, "none", existingId, source);

        var result = Assert.Single(import["results"]!.AsArray())!;
        Assert.True(
            (bool)result["success"]!,
            $"Leave files in place on an existing file-less entry failed: {result["error"]}");
        var saved = await Host.GetJsonAsync($"/api/v1/library/{existingId}");
        Assert.NotEmpty(saved["files"]!.AsArray());
    }

    [Theory]
    [InlineData("none")]
    [InlineData("move")]
    public async Task FailedFileImport_LeavesNoMonitoredFilelessEntryBehind(string action)
    {
        const string asin = "B0IS123602";
        var bookFolder = Path.Join(Host.ImportPath, "Issue Author", "Vanished Book");
        var source = CopySilence(bookFolder, "Vanished Book.m4b");
        var before = (await Host.GetLibraryAsync()).Count;

        // The page adds the match (monitored, the default), then imports its files; the file is gone by then
        // (moved away after the scan), so the import fails.
        var added = await AddResponseAsync(
            asin,
            "Vanished Book",
            action == "none" ? bookFolder : Host.LibraryPath);
        Assert.True(added.Status == HttpStatusCode.OK, $"Add: {(int)added.Status} {added.Body.ToJsonString()}");
        var addedId = (int)added.Body["audiobook"]!["id"]!;
        File.Delete(source);

        var import = await ManualImportAsync(bookFolder, action, addedId, source);
        var result = Assert.Single(import["results"]!.AsArray())!;
        Assert.False((bool)result["success"]!);

        var library = await Host.GetLibraryAsync();
        var leftover = library.SingleOrDefault(book => (string?)book!["asin"] == asin);
        Assert.True(
            leftover == null,
            $"The failed import ({result["error"]}) left an entry: monitored={leftover?["monitored"]}, "
            + $"basePath={leftover?["basePath"]}, files={leftover?["files"]?.AsArray().Count ?? 0}.");
        Assert.Equal(before, library.Count);
    }

    [Fact]
    public async Task FailedMoveImport_DoesNotBlockTheNextBookMovedIntoTheSameRoot()
    {
        // A move import whose file import fails: its entry keeps the root folder itself as BasePath.
        var vanishedFolder = Path.Join(Host.ImportPath, "Issue Author", "Vanished Book");
        var vanished = CopySilence(vanishedFolder, "Vanished Book.m4b");
        var leftoverId = await AddAsync("B0IS123605", "Vanished Book", Host.LibraryPath);
        File.Delete(vanished);
        var failed = await ManualImportAsync(vanishedFolder, "move", leftoverId, vanished);
        Assert.False((bool)failed["results"]![0]!["success"]!);

        // The next book of the same batch is added with the same destination, the selected root folder.
        var nextFolder = Path.Join(Host.ImportPath, "Issue Author", "Next Book");
        CopySilence(nextFolder, "Next Book.m4b");
        var next = await AddResponseAsync("B0IS123606", "Next Book", Host.LibraryPath);

        Assert.True(
            next.Status == HttpStatusCode.OK,
            $"The next book moved into the root is refused: {(int)next.Status} {next.Body.ToJsonString()}");
    }

    [Fact]
    public async Task LeaveInPlace_TwoBooksInOneFolder_SaysOneBookPerFolder()
    {
        // Two books whose files share one folder: the scan offers one row per book, same folder.
        var sharedFolder = Path.Join(Host.ImportPath, "Issue Author", "Two Books");
        var first = CopySilence(sharedFolder, "First Book.m4b");
        CopySilence(sharedFolder, "Second Book.m4b");

        var firstAdd = await AddResponseAsync("B0IS123603", "First Book", sharedFolder);
        Assert.True(firstAdd.Status == HttpStatusCode.OK, $"First add: {(int)firstAdd.Status} {firstAdd.Body.ToJsonString()}");
        var firstImport = await ManualImportAsync(sharedFolder, "none", (int)firstAdd.Body["audiobook"]!["id"]!, first);
        Assert.True((bool)firstImport["results"]![0]!["success"]!, $"First book: {firstImport["results"]![0]!["error"]}");

        var secondAdd = await AddResponseAsync("B0IS123604", "Second Book", sharedFolder);

        Assert.True(secondAdd.Status == HttpStatusCode.BadRequest, $"Second add: {(int)secondAdd.Status} {secondAdd.Body.ToJsonString()}");
        Assert.Equal("destination_path_blocked", (string?)secondAdd.Body["code"]);
        var message = (string?)secondAdd.Body["message"] ?? string.Empty;
        Assert.True(
            message.Contains("one book per folder", StringComparison.OrdinalIgnoreCase),
            $"The second book of a shared folder is refused with: \"{message}\".");
    }

    private static string CopySilence(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Join(folder, name);
        File.Copy(SilenceFile, path);
        return path;
    }

    private async Task<int> AddAsync(string asin, string title, string destinationPath)
    {
        var added = await AddResponseAsync(asin, title, destinationPath);
        Assert.True(added.Status == HttpStatusCode.OK, $"Add {title}: {(int)added.Status} {added.Body.ToJsonString()}");
        return (int)added.Body["audiobook"]!["id"]!;
    }

    private async Task<(HttpStatusCode Status, JsonNode Body)> AddResponseAsync(string asin, string title, string destinationPath)
    {
        // No authors: adding with authors starts author enrichment (see the seed in BackendIntegrationHost).
        using var response = await Host.SendAsync(HttpMethod.Post, "/api/v1/library/add", new
        {
            metadata = new { asin, title, authors = Array.Empty<string>(), region = "us", source = "Audible" },
            monitored = true,
            autoSearch = false,
            destinationPath,
        });
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonNode.Parse(text) ?? new JsonObject());
    }

    private async Task<JsonNode> ManualImportAsync(string folder, string action, int audiobookId, string source) =>
        await Host.SendJsonAsync(HttpMethod.Post, "/api/v1/library/manual-import", new
        {
            path = folder,
            mode = "interactive",
            action,
            includeCompanionFiles = action != "none",
            cleanupEmptySourceFolders = action == "move",
            items = new[] { new { fullPath = source, matchedAudiobookId = audiobookId } },
        });
}
