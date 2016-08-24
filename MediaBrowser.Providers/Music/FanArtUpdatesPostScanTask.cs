﻿using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommonIO;
using MediaBrowser.Providers.TV;

namespace MediaBrowser.Providers.Music
{
    class FanartUpdatesPostScanTask : ILibraryPostScanTask
    {
        private const string UpdatesUrl = "http://api.fanart.tv/webservice/newmusic/{0}/{1}/";

        /// <summary>
        /// The _HTTP client
        /// </summary>
        private readonly IHttpClient _httpClient;
        /// <summary>
        /// The _logger
        /// </summary>
        private readonly ILogger _logger;
        /// <summary>
        /// The _config
        /// </summary>
        private readonly IServerConfigurationManager _config;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IFileSystem _fileSystem;

        private static readonly CultureInfo UsCulture = new CultureInfo("en-US");

        public FanartUpdatesPostScanTask(IJsonSerializer jsonSerializer, IServerConfigurationManager config, ILogger logger, IHttpClient httpClient, IFileSystem fileSystem)
        {
            _jsonSerializer = jsonSerializer;
            _config = config;
            _logger = logger;
            _httpClient = httpClient;
            _fileSystem = fileSystem;
        }

        /// <summary>
        /// Runs the specified progress.
        /// </summary>
        /// <param name="progress">The progress.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var options = FanartSeriesProvider.Current.GetFanartOptions();

            if (!options.EnableAutomaticUpdates)
            {
                progress.Report(100);
                return;
            }

            var path = FanartArtistProvider.GetArtistDataPath(_config.CommonApplicationPaths);

            _fileSystem.CreateDirectory(path);

            var timestampFile = Path.Combine(path, "time.txt");

            var timestampFileInfo = _fileSystem.GetFileInfo(timestampFile);

            // Don't check for updates every single time
            if (timestampFileInfo.Exists && (DateTime.UtcNow - _fileSystem.GetLastWriteTimeUtc(timestampFileInfo)).TotalDays < 3)
            {
                return;
            }

            // Find out the last time we queried for updates
            var lastUpdateTime = timestampFileInfo.Exists ? _fileSystem.ReadAllText(timestampFile, Encoding.UTF8) : string.Empty;

            var existingDirectories = Directory.EnumerateDirectories(path).Select(Path.GetFileName).ToList();

            // If this is our first time, don't do any updates and just record the timestamp
            if (!string.IsNullOrEmpty(lastUpdateTime))
            {
                var artistsToUpdate = await GetArtistIdsToUpdate(existingDirectories, lastUpdateTime, options, cancellationToken).ConfigureAwait(false);

                progress.Report(5);

                await UpdateArtists(artistsToUpdate, progress, cancellationToken).ConfigureAwait(false);
            }

            var newUpdateTime = Convert.ToInt64(DateTimeToUnixTimestamp(DateTime.UtcNow)).ToString(UsCulture);

            _fileSystem.WriteAllText(timestampFile, newUpdateTime, Encoding.UTF8);

            progress.Report(100);
        }

        /// <summary>
        /// Gets the artist ids to update.
        /// </summary>
        /// <param name="existingArtistIds">The existing series ids.</param>
        /// <param name="lastUpdateTime">The last update time.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task{IEnumerable{System.String}}.</returns>
        private async Task<IEnumerable<string>> GetArtistIdsToUpdate(IEnumerable<string> existingArtistIds, string lastUpdateTime, FanartOptions options, CancellationToken cancellationToken)
        {
            var url = string.Format(UpdatesUrl, FanartArtistProvider.ApiKey, lastUpdateTime);

            if (!string.IsNullOrWhiteSpace(options.UserApiKey))
            {
                url += "&client_key=" + options.UserApiKey;
            }

            // First get last time
            using (var stream = await _httpClient.Get(new HttpRequestOptions
            {
                Url = url,
                CancellationToken = cancellationToken,
                EnableHttpCompression = true,
                ResourcePool = FanartArtistProvider.Current.FanArtResourcePool

            }).ConfigureAwait(false))
            {
                // If empty fanart will return a string of "null", rather than an empty list
                using (var reader = new StreamReader(stream))
                {
                    var json = await reader.ReadToEndAsync().ConfigureAwait(false);

                    if (string.Equals(json, "null", StringComparison.OrdinalIgnoreCase))
                    {
                        return new List<string>();
                    }

                    var existingDictionary = existingArtistIds.ToDictionary(i => i, StringComparer.OrdinalIgnoreCase);

                    var updates = _jsonSerializer.DeserializeFromString<List<FanArtUpdate>>(json);

                    return updates.Select(i => i.id).Where(existingDictionary.ContainsKey);
                }
            }
        }

        /// <summary>
        /// Updates the artists.
        /// </summary>
        /// <param name="idList">The id list.</param>
        /// <param name="progress">The progress.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        private async Task UpdateArtists(IEnumerable<string> idList, IProgress<double> progress, CancellationToken cancellationToken)
        {
            var list = idList.ToList();
            var numComplete = 0;

            foreach (var id in list)
            {
                await UpdateArtist(id, cancellationToken).ConfigureAwait(false);

                numComplete++;
                double percent = numComplete;
                percent /= list.Count;
                percent *= 95;

                progress.Report(percent + 5);
            }
        }

        /// <summary>
        /// Updates the artist.
        /// </summary>
        /// <param name="musicBrainzId">The musicBrainzId.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        private Task UpdateArtist(string musicBrainzId, CancellationToken cancellationToken)
        {
            _logger.Info("Updating artist " + musicBrainzId);

            return FanartArtistProvider.Current.DownloadArtistJson(musicBrainzId, cancellationToken);
        }

        /// <summary>
        /// Dates the time to unix timestamp.
        /// </summary>
        /// <param name="dateTime">The date time.</param>
        /// <returns>System.Double.</returns>
        private static double DateTimeToUnixTimestamp(DateTime dateTime)
        {
            return (dateTime - new DateTime(1970, 1, 1).ToUniversalTime()).TotalSeconds;
        }

        public class FanArtUpdate
        {
            public string id { get; set; }
            public string name { get; set; }
            public string new_images { get; set; }
            public string total_images { get; set; }
        }
    }
}
