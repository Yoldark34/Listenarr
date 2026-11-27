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
using System.Reflection;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Downloads.Monitoring
{
    /// <summary>
    /// Issue #1238: a torrent removed from the client keeps being polled every cycle.
    /// Orphan cleanup only considers Queued/Downloading/Paused, while the monitor's
    /// active set (IDownloadRepository.GetActiveAsync) also holds Processing,
    /// Completed, ImportPending and Moved downloads.
    /// </summary>
    [Trait("Name", "RemovedTorrentPollingTests")]
    [Trait("Category", "DownloadMonitorService")]
    public class RemovedTorrentPollingTests : BaseTests
    {
        private DownloadMonitorService downloadMonitorService = null!;
        private MethodInfo monitorDownloadsAsync = null!;
        private DownloadClientConfiguration client = null!;

        public override async Task InitializeAsync()
        {
            downloadMonitorService = _provider.GetRequiredService<DownloadMonitorService>();
            monitorDownloadsAsync = typeof(DownloadMonitorService)
                .GetMethod("MonitorDownloadsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            client = await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
                .WithType("mock")
                .WithName("Mock")
                .Build());
        }

        private async Task RunCycleAsync() =>
            await (Task)monitorDownloadsAsync.Invoke(downloadMonitorService, [CancellationToken.None])!;

        private async Task<Download> AddAsync(DownloadStatus status, string externalId) =>
            await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithId($"removed-{status}")
                .WithStatus(status)
                .WithExternalId(externalId)
                .WithStartDate(DateTime.UtcNow.AddHours(-2))
                .WithDownloadClientConfiguration(client)
                .Build());

        [Theory]
        [InlineData(DownloadStatus.Completed)]
        [InlineData(DownloadStatus.Processing)]
        [InlineData(DownloadStatus.ImportPending)]
        [InlineData(DownloadStatus.Moved)]
        public async Task Stops_polling_a_post_download_status_download_whose_torrent_was_removed_from_the_client(DownloadStatus status)
        {
            var download = await AddAsync(status, "C8B209B7REMOVEDFROMCLIENT");

            await RunCycleAsync();

            var stillActive = (await _downloadRepository.GetActiveAsync()).Any(d => d.Id == download.Id);
            Assert.False(stillActive,
                $"A {status} download whose torrent no longer exists in the client is still returned by GetActiveAsync, so the monitor polls it every cycle.");
        }

        [Fact]
        public async Task Stops_polling_a_downloading_download_whose_torrent_was_removed_from_the_client()
        {
            var download = await AddAsync(DownloadStatus.Downloading, "C8B209B7REMOVEDFROMCLIENT");

            await RunCycleAsync();

            Assert.Null(await _downloadRepository.GetByIdAsync(download.Id));
        }
    }
}
