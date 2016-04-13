﻿using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Threading;
using CommonIO;

namespace MediaBrowser.Controller.Entities.TV
{
    /// <summary>
    /// Class Episode
    /// </summary>
    public class Episode : Video, IHasTrailers, IHasSpecialFeatures, IHasLookupInfo<EpisodeInfo>, IHasSeries
    {


        public List<Guid> SpecialFeatureIds { get; set; }

        public Episode()
        {
            SpecialFeatureIds = new List<Guid>();

            RemoteTrailers = new List<MediaUrl>();
            LocalTrailerIds = new List<Guid>();
            RemoteTrailerIds = new List<Guid>();
        }

        public List<Guid> LocalTrailerIds { get; set; }
        public List<Guid> RemoteTrailerIds { get; set; }
        public List<MediaUrl> RemoteTrailers { get; set; }

        /// <summary>
        /// Gets the season in which it aired.
        /// </summary>
        /// <value>The aired season.</value>
        public int? AirsBeforeSeasonNumber { get; set; }
        public int? AirsAfterSeasonNumber { get; set; }
        public int? AirsBeforeEpisodeNumber { get; set; }

        /// <summary>
        /// Gets or sets the DVD season number.
        /// </summary>
        /// <value>The DVD season number.</value>
        public int? DvdSeasonNumber { get; set; }
        /// <summary>
        /// Gets or sets the DVD episode number.
        /// </summary>
        /// <value>The DVD episode number.</value>
        public float? DvdEpisodeNumber { get; set; }

        /// <summary>
        /// Gets or sets the absolute episode number.
        /// </summary>
        /// <value>The absolute episode number.</value>
        public int? AbsoluteEpisodeNumber { get; set; }

        /// <summary>
        /// This is the ending episode number for double episodes.
        /// </summary>
        /// <value>The index number.</value>
        public int? IndexNumberEnd { get; set; }

        [IgnoreDataMember]
        protected override bool SupportsOwnedItems
        {
            get
            {
                return IsStacked || MediaSourceCount > 1;
            }
        }

        [IgnoreDataMember]
        public int? AiredSeasonNumber
        {
            get
            {
                return AirsAfterSeasonNumber ?? AirsBeforeSeasonNumber ?? PhysicalSeasonNumber;
            }
        }

        [IgnoreDataMember]
        public int? PhysicalSeasonNumber
        {
            get
            {
                var value = ParentIndexNumber;

                if (value.HasValue)
                {
                    return value;
                }

                var season = Season;

                return season != null ? season.IndexNumber : null;
            }
        }

        [IgnoreDataMember]
        public override Folder LatestItemsIndexContainer
        {
            get
            {
                return Series;
            }
        }

        [IgnoreDataMember]
        public override Guid? DisplayParentId
        {
            get
            {
                return SeasonId;
            }
        }

        /// <summary>
        /// Gets the user data key.
        /// </summary>
        /// <returns>System.String.</returns>
        protected override string CreateUserDataKey()
        {
            var series = Series;

            if (series != null && ParentIndexNumber.HasValue && IndexNumber.HasValue)
            {
                return series.GetUserDataKey() + ParentIndexNumber.Value.ToString("000") + IndexNumber.Value.ToString("000");
            }

            return base.CreateUserDataKey();
        }

        /// <summary>
        /// This Episode's Series Instance
        /// </summary>
        /// <value>The series.</value>
        [IgnoreDataMember]
        public Series Series
        {
            get { return FindParent<Series>(); }
        }

        [IgnoreDataMember]
        public Season Season
        {
            get
            {
                var season = FindParent<Season>();

                // Episodes directly in series folder
                if (season == null)
                {
                    var series = Series;

                    if (series != null && ParentIndexNumber.HasValue)
                    {
                        var findNumber = ParentIndexNumber.Value;

                        season = series.Children
                            .OfType<Season>()
                            .FirstOrDefault(i => i.IndexNumber.HasValue && i.IndexNumber.Value == findNumber);
                    }
                }

                return season;
            }
        }

        [IgnoreDataMember]
        public bool IsInSeasonFolder
        {
            get
            {
                return FindParent<Season>() != null;
            }
        }

        [IgnoreDataMember]
        public string SeriesName
        {
            get
            {
                var series = Series;
                return series == null ? null : series.Name;
            }
        }

        /// <summary>
        /// Creates the name of the sort.
        /// </summary>
        /// <returns>System.String.</returns>
        protected override string CreateSortName()
        {
            return (ParentIndexNumber != null ? ParentIndexNumber.Value.ToString("000 - ") : "")
                    + (IndexNumber != null ? IndexNumber.Value.ToString("0000 - ") : "") + Name;
        }

        /// <summary>
        /// Determines whether [contains episode number] [the specified number].
        /// </summary>
        /// <param name="number">The number.</param>
        /// <returns><c>true</c> if [contains episode number] [the specified number]; otherwise, <c>false</c>.</returns>
        public bool ContainsEpisodeNumber(int number)
        {
            if (IndexNumber.HasValue)
            {
                if (IndexNumberEnd.HasValue)
                {
                    return number >= IndexNumber.Value && number <= IndexNumberEnd.Value;
                }

                return IndexNumber.Value == number;
            }

            return false;
        }

        [IgnoreDataMember]
        public override bool SupportsRemoteImageDownloading
        {
            get
            {
                if (IsMissingEpisode)
                {
                    return false;
                }

                return true;
            }
        }

        [IgnoreDataMember]
        public bool IsMissingEpisode
        {
            get
            {
                return LocationType == LocationType.Virtual && !IsUnaired;
            }
        }

        [IgnoreDataMember]
        public bool IsUnaired
        {
            get { return PremiereDate.HasValue && PremiereDate.Value.ToLocalTime().Date >= DateTime.Now.Date; }
        }

        [IgnoreDataMember]
        public bool IsVirtualUnaired
        {
            get { return LocationType == LocationType.Virtual && IsUnaired; }
        }

        [IgnoreDataMember]
        public Guid? SeasonId
        {
            get
            {
                // First see if the parent is a Season
                var season = Season;

                if (season != null)
                {
                    return season.Id;
                }

                return null;
            }
        }

        public override IEnumerable<Guid> GetAncestorIds()
        {
            var list = base.GetAncestorIds().ToList();

            var seasonId = SeasonId;

            if (seasonId.HasValue && !list.Contains(seasonId.Value))
            {
                list.Add(seasonId.Value);
            }

            return list;
        }

        public override IEnumerable<string> GetDeletePaths()
        {
            return new[] { Path };
        }

        public override UnratedItem GetBlockUnratedType()
        {
            return UnratedItem.Series;
        }

        public EpisodeInfo GetLookupInfo()
        {
            var id = GetItemLookupInfo<EpisodeInfo>();

            var series = Series;

            if (series != null)
            {
                id.SeriesProviderIds = series.ProviderIds;
                id.AnimeSeriesIndex = series.AnimeSeriesIndex;
            }

            id.IndexNumberEnd = IndexNumberEnd;

            return id;
        }

        public override bool BeforeMetadataRefresh()
        {
            var hasChanges = base.BeforeMetadataRefresh();

            try
            {
                if (LibraryManager.FillMissingEpisodeNumbersFromPath(this))
                {
                    hasChanges = true;
                }
            }
            catch (Exception ex)
            {
                Logger.ErrorException("Error in FillMissingEpisodeNumbersFromPath. Episode: {0}", ex, Path ?? Name ?? Id.ToString());
            }

            return hasChanges;
        }

        public static string GetEpisodeUserDataKey(BaseItem episode)
        {
            var key = episode.GetProviderId(MetadataProviders.Tmdb);

            if (string.IsNullOrWhiteSpace(key))
            {
                key = episode.GetProviderId(MetadataProviders.Imdb);
            }

            return key;
        }


        /// <summary>
        /// Gets the trailer ids.
        /// </summary>
        /// <returns>List&lt;Guid&gt;.</returns>
        public List<Guid> GetTrailerIds()
        {
            var list = LocalTrailerIds.ToList();
            list.AddRange(RemoteTrailerIds);
            return list;
        }


        protected override async Task<bool> RefreshedOwnedItems(MetadataRefreshOptions options, List<FileSystemMetadata> fileSystemChildren, CancellationToken cancellationToken)
        {
            var hasChanges = await base.RefreshedOwnedItems(options, fileSystemChildren, cancellationToken).ConfigureAwait(false);

            // Must have a parent to have special features
            // In other words, it must be part of the Parent/Child tree
            // if (LocationType == LocationType.FileSystem && Parent != null && !IsInMixedFolder)
            // {
            var specialFeaturesChanged = await RefreshSpecialFeatures(options, fileSystemChildren, cancellationToken).ConfigureAwait(false);

            if (specialFeaturesChanged)
            {
                hasChanges = true;
            }
            // }

            return hasChanges;
        }

        private async Task<bool> RefreshSpecialFeatures(MetadataRefreshOptions options, List<FileSystemMetadata> fileSystemChildren, CancellationToken cancellationToken)
        {
            var newItems = LibraryManager.FindExtras(this, fileSystemChildren, options.DirectoryService).ToList();
            var newItemIds = newItems.Select(i => i.Id).ToList();

            var itemsChanged = !SpecialFeatureIds.SequenceEqual(newItemIds);

            var tasks = newItems.Select(i => i.RefreshMetadata(options, cancellationToken));

            await Task.WhenAll(tasks).ConfigureAwait(false);

            SpecialFeatureIds = newItemIds;

            return itemsChanged;
        }


    }
}
