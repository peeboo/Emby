﻿using MediaBrowser.Common.Progress;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CommonIO;
using MediaBrowser.Model.Channels;

namespace MediaBrowser.Controller.Entities
{
    /// <summary>
    /// Class Folder
    /// </summary>
    public class Folder : BaseItem, IHasThemeMedia, IHasTags
    {
        public static IUserManager UserManager { get; set; }
        public static IUserViewManager UserViewManager { get; set; }

        public List<Guid> ThemeSongIds { get; set; }
        public List<Guid> ThemeVideoIds { get; set; }

        public Folder()
        {
            LinkedChildren = new List<LinkedChild>();

            ThemeSongIds = new List<Guid>();
            ThemeVideoIds = new List<Guid>();
        }

        [IgnoreDataMember]
        public virtual bool IsPreSorted
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance is folder.
        /// </summary>
        /// <value><c>true</c> if this instance is folder; otherwise, <c>false</c>.</value>
        [IgnoreDataMember]
        public override bool IsFolder
        {
            get
            {
                return true;
            }
        }

        [IgnoreDataMember]
        public override string FileNameWithoutExtension
        {
            get
            {
                if (LocationType == LocationType.FileSystem)
                {
                    return System.IO.Path.GetFileName(Path);
                }

                return null;
            }
        }

        protected override bool IsAllowTagFilterEnforced()
        {
            if (this is ICollectionFolder)
            {
                return false;
            }
            if (this is UserView)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is physical root.
        /// </summary>
        /// <value><c>true</c> if this instance is physical root; otherwise, <c>false</c>.</value>
        public bool IsPhysicalRoot { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether this instance is root.
        /// </summary>
        /// <value><c>true</c> if this instance is root; otherwise, <c>false</c>.</value>
        public bool IsRoot { get; set; }

        /// <summary>
        /// Gets a value indicating whether this instance is virtual folder.
        /// </summary>
        /// <value><c>true</c> if this instance is virtual folder; otherwise, <c>false</c>.</value>
        [IgnoreDataMember]
        public virtual bool IsVirtualFolder
        {
            get
            {
                return false;
            }
        }

        public virtual List<LinkedChild> LinkedChildren { get; set; }

        [IgnoreDataMember]
        protected virtual bool SupportsShortcutChildren
        {
            get { return false; }
        }

        /// <summary>
        /// Adds the child.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="System.InvalidOperationException">Unable to add  + item.Name</exception>
        public async Task AddChild(BaseItem item, CancellationToken cancellationToken)
        {
            item.SetParent(this);

            if (item.Id == Guid.Empty)
            {
                item.Id = LibraryManager.GetNewItemId(item.Path, item.GetType());
            }

            if (ActualChildren.Any(i => i.Id == item.Id))
            {
                throw new ArgumentException(string.Format("A child with the Id {0} already exists.", item.Id));
            }

            if (item.DateCreated == DateTime.MinValue)
            {
                item.DateCreated = DateTime.UtcNow;
            }
            if (item.DateModified == DateTime.MinValue)
            {
                item.DateModified = DateTime.UtcNow;
            }

            AddChildInternal(item.Id);

            await LibraryManager.CreateItem(item, cancellationToken).ConfigureAwait(false);
        }

        protected void AddChildrenInternal(List<Guid> children)
        {
            lock (_childrenSyncLock)
            {
                var newChildren = ChildIds.ToList();
                newChildren.AddRange(children);
                _children = newChildren.ToList();
            }
        }
        protected void AddChildInternal(Guid child)
        {
            lock (_childrenSyncLock)
            {
                var childIds = ChildIds.ToList();
                if (!childIds.Contains(child))
                {
                    childIds.Add(child);
                    _children = childIds.ToList();
                }
            }
        }

        protected void RemoveChildrenInternal(List<Guid> children)
        {
            lock (_childrenSyncLock)
            {
                _children = ChildIds.Except(children).ToList();
            }
        }

        /// <summary>
        /// Removes the child.
        /// </summary>
        /// <param name="item">The item.</param>
        public void RemoveChild(BaseItem item)
        {
            RemoveChildrenInternal(new[] { item.Id }.ToList());

            item.SetParent(null);
        }

        #region Indexing

        /// <summary>
        /// Returns the valid set of index by options for this folder type.
        /// Override or extend to modify.
        /// </summary>
        /// <returns>Dictionary{System.StringFunc{UserIEnumerable{BaseItem}}}.</returns>
        protected virtual IEnumerable<string> GetIndexByOptions()
        {
            return new List<string> {            
                {"None"}, 
                {"Performer"},
                {"Genre"},
                {"Director"},
                {"Year"},
                {"Studio"}
            };
        }

        /// <summary>
        /// Get the list of indexy by choices for this folder (localized).
        /// </summary>
        /// <value>The index by option strings.</value>
        [IgnoreDataMember]
        public IEnumerable<string> IndexByOptionStrings
        {
            get { return GetIndexByOptions(); }
        }

        #endregion

        /// <summary>
        /// The children
        /// </summary>
        private IReadOnlyList<Guid> _children;
        /// <summary>
        /// The _children sync lock
        /// </summary>
        private readonly object _childrenSyncLock = new object();
        /// <summary>
        /// Gets or sets the actual children.
        /// </summary>
        /// <value>The actual children.</value>
        protected virtual IEnumerable<Guid> ChildIds
        {
            get
            {
                lock (_childrenSyncLock)
                {
                    if (_children == null)
                    {
                        _children = LoadChildren().ToList();
                    }
                    return _children.ToList();
                }
            }
        }

        /// <summary>
        /// Gets the actual children.
        /// </summary>
        /// <value>The actual children.</value>
        protected virtual IEnumerable<BaseItem> ActualChildren
        {
            get
            {
                return ChildIds.Select(LibraryManager.GetItemById).Where(i => i != null);
            }
        }

        /// <summary>
        /// thread-safe access to the actual children of this folder - without regard to user
        /// </summary>
        /// <value>The children.</value>
        [IgnoreDataMember]
        public IEnumerable<BaseItem> Children
        {
            get { return ActualChildren.ToList(); }
        }

        /// <summary>
        /// thread-safe access to all recursive children of this folder - without regard to user
        /// </summary>
        /// <value>The recursive children.</value>
        [IgnoreDataMember]
        public IEnumerable<BaseItem> RecursiveChildren
        {
            get { return GetRecursiveChildren(); }
        }

        public override bool IsVisible(User user)
        {
            if (this is ICollectionFolder && !(this is BasePluginFolder))
            {
                if (user.Policy.BlockedMediaFolders != null)
                {
                    if (user.Policy.BlockedMediaFolders.Contains(Id.ToString("N"), StringComparer.OrdinalIgnoreCase) ||

                        // Backwards compatibility
                        user.Policy.BlockedMediaFolders.Contains(Name, StringComparer.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!user.Policy.EnableAllFolders && !user.Policy.EnabledFolders.Contains(Id.ToString("N"), StringComparer.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }

            return base.IsVisible(user);
        }

        /// <summary>
        /// Loads our children.  Validation will occur externally.
        /// We want this sychronous.
        /// </summary>
        protected virtual IEnumerable<Guid> LoadChildren()
        {
            //just load our children from the repo - the library will be validated and maintained in other processes
            return GetCachedChildren();
        }

        public Task ValidateChildren(IProgress<double> progress, CancellationToken cancellationToken)
        {
            return ValidateChildren(progress, cancellationToken, new MetadataRefreshOptions(new DirectoryService(FileSystem)));
        }

        /// <summary>
        /// Validates that the children of the folder still exist
        /// </summary>
        /// <param name="progress">The progress.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="metadataRefreshOptions">The metadata refresh options.</param>
        /// <param name="recursive">if set to <c>true</c> [recursive].</param>
        /// <returns>Task.</returns>
        public Task ValidateChildren(IProgress<double> progress, CancellationToken cancellationToken, MetadataRefreshOptions metadataRefreshOptions, bool recursive = true)
        {
            return ValidateChildrenInternal(progress, cancellationToken, recursive, true, metadataRefreshOptions, metadataRefreshOptions.DirectoryService);
        }

        private Dictionary<Guid, BaseItem> GetActualChildrenDictionary()
        {
            var dictionary = new Dictionary<Guid, BaseItem>();

            foreach (var child in ActualChildren)
            {
                var id = child.Id;
                if (dictionary.ContainsKey(id))
                {
                    Logger.Error("Found folder containing items with duplicate id. Path: {0}, Child Name: {1}",
                        Path ?? Name,
                        child.Path ?? child.Name);
                }
                else
                {
                    dictionary[id] = child;
                }
            }

            return dictionary;
        }

        private bool IsValidFromResolver(BaseItem current, BaseItem newItem)
        {
            return current.IsValidFromResolver(newItem);
        }

        /// <summary>
        /// Validates the children internal.
        /// </summary>
        /// <param name="progress">The progress.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="recursive">if set to <c>true</c> [recursive].</param>
        /// <param name="refreshChildMetadata">if set to <c>true</c> [refresh child metadata].</param>
        /// <param name="refreshOptions">The refresh options.</param>
        /// <param name="directoryService">The directory service.</param>
        /// <returns>Task.</returns>
        protected async virtual Task ValidateChildrenInternal(IProgress<double> progress, CancellationToken cancellationToken, bool recursive, bool refreshChildMetadata, MetadataRefreshOptions refreshOptions, IDirectoryService directoryService)
        {
            var locationType = LocationType;

            cancellationToken.ThrowIfCancellationRequested();

            var validChildren = new List<BaseItem>();

            if (locationType != LocationType.Remote && locationType != LocationType.Virtual)
            {
                IEnumerable<BaseItem> nonCachedChildren;

                try
                {
                    nonCachedChildren = GetNonCachedChildren(directoryService);
                }
                catch (IOException ex)
                {
                    nonCachedChildren = new BaseItem[] { };

                    Logger.ErrorException("Error getting file system entries for {0}", ex, Path);
                }

                if (nonCachedChildren == null) return; //nothing to validate

                progress.Report(5);

                //build a dictionary of the current children we have now by Id so we can compare quickly and easily
                var currentChildren = GetActualChildrenDictionary();

                //create a list for our validated children
                var newItems = new List<BaseItem>();

                cancellationToken.ThrowIfCancellationRequested();

                foreach (var child in nonCachedChildren)
                {
                    BaseItem currentChild;

                    if (currentChildren.TryGetValue(child.Id, out currentChild) && IsValidFromResolver(currentChild, child))
                    {
                        var currentChildLocationType = currentChild.LocationType;
                        if (currentChildLocationType != LocationType.Remote &&
                            currentChildLocationType != LocationType.Virtual)
                        {
                            currentChild.DateModified = child.DateModified;
                        }

                        await UpdateIsOffline(currentChild, false).ConfigureAwait(false);
                        validChildren.Add(currentChild);

                        continue;
                    }

                    // Brand new item - needs to be added
                    child.SetParent(this);
                    newItems.Add(child);
                    validChildren.Add(child);
                }

                // If any items were added or removed....
                if (newItems.Count > 0 || currentChildren.Count != validChildren.Count)
                {
                    // That's all the new and changed ones - now see if there are any that are missing
                    var itemsRemoved = currentChildren.Values.Except(validChildren).ToList();
                    var actualRemovals = new List<BaseItem>();

                    foreach (var item in itemsRemoved)
                    {
                        if (item.LocationType == LocationType.Virtual ||
                            item.LocationType == LocationType.Remote)
                        {
                            // Don't remove these because there's no way to accurately validate them.
                            validChildren.Add(item);
                        }

                        else if (!string.IsNullOrEmpty(item.Path) && IsPathOffline(item.Path))
                        {
                            await UpdateIsOffline(item, true).ConfigureAwait(false);
                            validChildren.Add(item);
                        }
                        else
                        {
                            actualRemovals.Add(item);
                        }
                    }

                    if (actualRemovals.Count > 0)
                    {
                        RemoveChildrenInternal(actualRemovals.Select(i => i.Id).ToList());

                        foreach (var item in actualRemovals)
                        {
                            Logger.Debug("Removed item: " + item.Path);

                            item.SetParent(null);
                            item.IsOffline = false;
                            await LibraryManager.DeleteItem(item, new DeleteOptions { DeleteFileLocation = false }).ConfigureAwait(false);
                            LibraryManager.ReportItemRemoved(item);
                        }
                    }

                    await LibraryManager.CreateItems(newItems, cancellationToken).ConfigureAwait(false);

                    AddChildrenInternal(newItems.Select(i => i.Id).ToList());
                }
            }

            progress.Report(10);

            cancellationToken.ThrowIfCancellationRequested();

            if (recursive)
            {
                await ValidateSubFolders(ActualChildren.OfType<Folder>().ToList(), directoryService, progress, cancellationToken).ConfigureAwait(false);
            }

            progress.Report(20);

            if (refreshChildMetadata)
            {
                var container = this as IMetadataContainer;

                var innerProgress = new ActionableProgress<double>();

                innerProgress.RegisterAction(p => progress.Report(.80 * p + 20));

                if (container != null)
                {
                    await container.RefreshAllMetadata(refreshOptions, innerProgress, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await RefreshMetadataRecursive(refreshOptions, recursive, innerProgress, cancellationToken);
                }
            }

            progress.Report(100);
        }

        private Task UpdateIsOffline(BaseItem item, bool newValue)
        {
            if (item.IsOffline != newValue)
            {
                item.IsOffline = newValue;
                return item.UpdateToRepository(ItemUpdateType.None, CancellationToken.None);
            }

            return Task.FromResult(true);
        }

        private async Task RefreshMetadataRecursive(MetadataRefreshOptions refreshOptions, bool recursive, IProgress<double> progress, CancellationToken cancellationToken)
        {
            var children = ActualChildren.ToList();

            var percentages = new Dictionary<Guid, double>(children.Count);
            var numComplete = 0;
            var count = children.Count;

            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (child.IsFolder)
                {
                    var innerProgress = new ActionableProgress<double>();

                    // Avoid implicitly captured closure
                    var currentChild = child;
                    innerProgress.RegisterAction(p =>
                    {
                        lock (percentages)
                        {
                            percentages[currentChild.Id] = p / 100;

                            var innerPercent = percentages.Values.Sum();
                            innerPercent /= count;
                            innerPercent *= 100;
                            progress.Report(innerPercent);
                        }
                    });

                    await RefreshChildMetadata(child, refreshOptions, recursive, innerProgress, cancellationToken)
                      .ConfigureAwait(false);
                }
                else
                {
                    await RefreshChildMetadata(child, refreshOptions, false, new Progress<double>(), cancellationToken)
                      .ConfigureAwait(false);
                }

                numComplete++;
                double percent = numComplete;
                percent /= count;
                percent *= 100;

                progress.Report(percent);
            }

            progress.Report(100);
        }

        private async Task RefreshChildMetadata(BaseItem child, MetadataRefreshOptions refreshOptions, bool recursive, IProgress<double> progress, CancellationToken cancellationToken)
        {
            var container = child as IMetadataContainer;

            if (container != null)
            {
                await container.RefreshAllMetadata(refreshOptions, progress, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await child.RefreshMetadata(refreshOptions, cancellationToken).ConfigureAwait(false);

                if (recursive)
                {
                    var folder = child as Folder;

                    if (folder != null)
                    {
                        await folder.RefreshMetadataRecursive(refreshOptions, true, progress, cancellationToken);
                    }
                }
            }
            progress.Report(100);
        }

        /// <summary>
        /// Refreshes the children.
        /// </summary>
        /// <param name="children">The children.</param>
        /// <param name="directoryService">The directory service.</param>
        /// <param name="progress">The progress.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        private async Task ValidateSubFolders(IList<Folder> children, IDirectoryService directoryService, IProgress<double> progress, CancellationToken cancellationToken)
        {
            var list = children;
            var childCount = list.Count;

            var percentages = new Dictionary<Guid, double>(list.Count);

            foreach (var item in list)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var child = item;

                var innerProgress = new ActionableProgress<double>();

                innerProgress.RegisterAction(p =>
                {
                    lock (percentages)
                    {
                        percentages[child.Id] = p / 100;

                        var percent = percentages.Values.Sum();
                        percent /= childCount;

                        progress.Report(10 * percent + 10);
                    }
                });

                await child.ValidateChildrenInternal(innerProgress, cancellationToken, true, false, null, directoryService)
                        .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Determines whether the specified path is offline.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns><c>true</c> if the specified path is offline; otherwise, <c>false</c>.</returns>
        public static bool IsPathOffline(string path)
        {
            if (FileSystem.FileExists(path))
            {
                return false;
            }

            var originalPath = path;

            // Depending on whether the path is local or unc, it may return either null or '\' at the top
            while (!string.IsNullOrEmpty(path) && path.Length > 1)
            {
                if (FileSystem.DirectoryExists(path))
                {
                    return false;
                }

                path = System.IO.Path.GetDirectoryName(path);
            }

            if (ContainsPath(LibraryManager.GetVirtualFolders(), originalPath))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether the specified folders contains path.
        /// </summary>
        /// <param name="folders">The folders.</param>
        /// <param name="path">The path.</param>
        /// <returns><c>true</c> if the specified folders contains path; otherwise, <c>false</c>.</returns>
        private static bool ContainsPath(IEnumerable<VirtualFolderInfo> folders, string path)
        {
            return folders.SelectMany(i => i.Locations).Any(i => ContainsPath(i, path));
        }

        private static bool ContainsPath(string parent, string path)
        {
            return string.Equals(parent, path, StringComparison.OrdinalIgnoreCase) || FileSystem.ContainsSubPath(parent, path);
        }

        /// <summary>
        /// Get the children of this folder from the actual file system
        /// </summary>
        /// <returns>IEnumerable{BaseItem}.</returns>
        protected virtual IEnumerable<BaseItem> GetNonCachedChildren(IDirectoryService directoryService)
        {
            var collectionType = LibraryManager.GetContentType(this);

            return LibraryManager.ResolvePaths(GetFileSystemChildren(directoryService), directoryService, this, collectionType);
        }

        /// <summary>
        /// Get our children from the repo - stubbed for now
        /// </summary>
        /// <returns>IEnumerable{BaseItem}.</returns>
        protected IEnumerable<Guid> GetCachedChildren()
        {
            return ItemRepository.GetItemIdsList(new InternalItemsQuery
            {
                ParentId = Id

            });
        }

        public QueryResult<BaseItem> QueryRecursive(InternalItemsQuery query)
        {
            var user = query.User;

            if (RequiresPostFiltering(query))
            {
                IEnumerable<BaseItem> items;
                Func<BaseItem, bool> filter = i => UserViewBuilder.Filter(i, user, query, UserDataManager, LibraryManager);

                if (query.User == null)
                {
                    items = GetRecursiveChildren(filter);
                }
                else
                {
                    items = GetRecursiveChildren(user, filter);
                }

                return PostFilterAndSort(items, query);
            }

            if (!(this is UserRootFolder) && !(this is AggregateFolder))
            {
                query.ParentId = query.ParentId ?? Id;
            }

            return LibraryManager.GetItemsResult(query);
        }

        private bool RequiresPostFiltering(InternalItemsQuery query)
        {
            if (LinkedChildren.Count > 0)
            {
                if (!(this is ICollectionFolder))
                {
                    Logger.Debug("Query requires post-filtering due to LinkedChildren");
                    return true;
                }
            }

            if (query.SortBy != null && query.SortBy.Length > 0)
            {
                if (query.SortBy.Contains(ItemSortBy.DatePlayed, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.DatePlayed");
                    return true;
                }
                if (query.SortBy.Contains(ItemSortBy.IsFavoriteOrLiked, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.IsFavoriteOrLiked");
                    return true;
                }
                if (query.SortBy.Contains(ItemSortBy.IsPlayed, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.IsPlayed");
                    return true;
                }
                if (query.SortBy.Contains(ItemSortBy.IsUnplayed, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.IsUnplayed");
                    return true;
                }
                if (query.SortBy.Contains(ItemSortBy.AiredEpisodeOrder, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.AiredEpisodeOrder");
                    return true;
                }
                if (query.SortBy.Contains(ItemSortBy.Album, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.Album");
                    return true;
                }
                if (query.SortBy.Contains(ItemSortBy.AlbumArtist, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.AlbumArtist");
                    return true;
                }
                if (query.SortBy.Contains(ItemSortBy.Artist, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.Artist");
                    return true;
                }
                if (query.SortBy.Contains(ItemSortBy.Budget, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.Budget");
                    return true;
                }
                if (query.SortBy.Contains(ItemSortBy.DateLastContentAdded, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.DateLastContentAdded");
                    return true;
                }
                if (query.SortBy.Contains(ItemSortBy.GameSystem, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.GameSystem");
                    return true;
                }
                if (query.SortBy.Contains(ItemSortBy.Metascore, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.Metascore");
                    return true;
                }
                if (query.SortBy.Contains(ItemSortBy.OfficialRating, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.OfficialRating");
                    return true;
                }
                if (query.SortBy.Contains(ItemSortBy.PlayCount, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.PlayCount");
                    return true;
                }
                if (query.SortBy.Contains(ItemSortBy.Players, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.Players");
                    return true;
                }
                if (query.SortBy.Contains(ItemSortBy.Revenue, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.Revenue");
                    return true;
                }
                if (query.SortBy.Contains(ItemSortBy.SeriesSortName, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.SeriesSortName");
                    return true;
                }
                if (query.SortBy.Contains(ItemSortBy.StartDate, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.StartDate");
                    return true;
                }
                if (query.SortBy.Contains(ItemSortBy.Studio, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.Studio");
                    return true;
                }
                if (query.SortBy.Contains(ItemSortBy.VideoBitRate, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.Debug("Query requires post-filtering due to ItemSortBy.VideoBitRate");
                    return true;
                }
            }

            if (query.ItemIds.Length > 0)
            {
                Logger.Debug("Query requires post-filtering due to ItemIds");
                return true;
            }

            if (query.PersonIds.Length > 0)
            {
                Logger.Debug("Query requires post-filtering due to PersonIds");
                return true;
            }

            if (query.IsLiked.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to IsLiked");
                return true;
            }

            if (query.IsFavoriteOrLiked.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to IsFavoriteOrLiked");
                return true;
            }

            if (query.IsFavorite.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to IsFavorite");
                return true;
            }

            if (query.IsResumable.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to IsResumable");
                return true;
            }

            if (query.IsPlayed.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to IsPlayed");
                return true;
            }

            if (query.IsInBoxSet.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to IsInBoxSet");
                return true;
            }

            // Filter by Video3DFormat
            if (query.Is3D.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to Is3D");
                return true;
            }

            if (query.HasImdbId.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to HasImdbId");
                return true;
            }

            if (query.HasTmdbId.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to HasTmdbId");
                return true;
            }

            if (query.HasTvdbId.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to HasTvdbId");
                return true;
            }

            if (query.IsYearMismatched.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to IsYearMismatched");
                return true;
            }

            if (query.HasOfficialRating.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to HasOfficialRating");
                return true;
            }

            if (query.IsPlaceHolder.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to IsPlaceHolder");
                return true;
            }

            if (query.HasSpecialFeature.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to HasSpecialFeature");
                return true;
            }

            if (query.HasSubtitles.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to HasSubtitles");
                return true;
            }

            if (query.HasTrailer.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to HasTrailer");
                return true;
            }

            if (query.HasThemeSong.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to HasThemeSong");
                return true;
            }

            if (query.HasThemeVideo.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to HasThemeVideo");
                return true;
            }

            // Filter by VideoType
            if (query.VideoTypes.Length > 0)
            {
                Logger.Debug("Query requires post-filtering due to VideoTypes");
                return true;
            }

            if (query.ImageTypes.Length > 0)
            {
                Logger.Debug("Query requires post-filtering due to ImageTypes");
                return true;
            }

            // Apply studio filter
            if (query.StudioIds.Length > 0)
            {
                Logger.Debug("Query requires post-filtering due to StudioIds");
                return true;
            }

            // Apply genre filter
            if (query.GenreIds.Length > 0)
            {
                Logger.Debug("Query requires post-filtering due to GenreIds");
                return true;
            }

            // Apply person filter
            if (query.ItemIdsFromPersonFilters != null)
            {
                Logger.Debug("Query requires post-filtering due to ItemIdsFromPersonFilters");
                return true;
            }

            if (query.MinPlayers.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to MinPlayers");
                return true;
            }

            if (query.MaxPlayers.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to MaxPlayers");
                return true;
            }

            if (query.OfficialRatings.Length > 0)
            {
                Logger.Debug("Query requires post-filtering due to OfficialRatings");
                return true;
            }

            if (query.IsMissing.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to IsMissing");
                return true;
            }

            if (query.IsUnaired.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to IsUnaired");
                return true;
            }

            if (query.IsVirtualUnaired.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to IsVirtualUnaired");
                return true;
            }

            if (UserViewBuilder.CollapseBoxSetItems(query, this, query.User))
            {
                Logger.Debug("Query requires post-filtering due to CollapseBoxSetItems");
                return true;
            }

            if (!string.IsNullOrWhiteSpace(query.AdjacentTo))
            {
                Logger.Debug("Query requires post-filtering due to AdjacentTo");
                return true;
            }

            if (!string.IsNullOrWhiteSpace(query.NameContains))
            {
                Logger.Debug("Query requires post-filtering due to NameContains");
                return true;
            }

            if (!string.IsNullOrWhiteSpace(query.NameLessThan))
            {
                Logger.Debug("Query requires post-filtering due to NameLessThan");
                return true;
            }

            if (!string.IsNullOrWhiteSpace(query.NameStartsWith))
            {
                Logger.Debug("Query requires post-filtering due to NameStartsWith");
                return true;
            }

            if (!string.IsNullOrWhiteSpace(query.NameStartsWithOrGreater))
            {
                Logger.Debug("Query requires post-filtering due to NameStartsWithOrGreater");
                return true;
            }

            if (query.AirDays.Length > 0)
            {
                Logger.Debug("Query requires post-filtering due to AirDays");
                return true;
            }

            if (query.SeriesStatuses.Length > 0)
            {
                Logger.Debug("Query requires post-filtering due to SeriesStatuses");
                return true;
            }

            if (query.AiredDuringSeason.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to AiredDuringSeason");
                return true;
            }

            if (!string.IsNullOrWhiteSpace(query.AlbumArtistStartsWithOrGreater))
            {
                Logger.Debug("Query requires post-filtering due to AlbumArtistStartsWithOrGreater");
                return true;
            }

            if (query.AlbumNames.Length > 0)
            {
                Logger.Debug("Query requires post-filtering due to AlbumNames");
                return true;
            }

            if (query.ArtistNames.Length > 0)
            {
                Logger.Debug("Query requires post-filtering due to ArtistNames");
                return true;
            }

            return false;
        }

        public Task<QueryResult<BaseItem>> GetItems(InternalItemsQuery query)
        {
            if (query.ItemIds.Length > 0)
            {
                var specificItems = query.ItemIds.Select(LibraryManager.GetItemById).Where(i => i != null).ToList();
                return Task.FromResult(PostFilterAndSort(specificItems, query));
            }

            return GetItemsInternal(query);
        }

        protected virtual async Task<QueryResult<BaseItem>> GetItemsInternal(InternalItemsQuery query)
        {
            if (SourceType == SourceType.Channel)
            {
                try
                {
                    // Don't blow up here because it could cause parent screens with other content to fail
                    return await ChannelManager.GetChannelItemsInternal(new ChannelItemQuery
                    {
                        ChannelId = ChannelId,
                        FolderId = Id.ToString("N"),
                        Limit = query.Limit,
                        StartIndex = query.StartIndex,
                        UserId = query.User.Id.ToString("N"),
                        SortBy = query.SortBy,
                        SortOrder = query.SortOrder

                    }, new Progress<double>(), CancellationToken.None);
                }
                catch
                {
                    // Already logged at lower levels
                    return new QueryResult<BaseItem>
                    {

                    };
                }
            }

            if (query.Recursive)
            {
                return QueryRecursive(query);
            }

            var user = query.User;

            Func<BaseItem, bool> filter = i => UserViewBuilder.Filter(i, user, query, UserDataManager, LibraryManager);

            IEnumerable<BaseItem> items;

            if (query.User == null)
            {
                items = query.Recursive
                   ? GetRecursiveChildren(filter)
                   : Children.Where(filter);
            }
            else
            {
                items = query.Recursive
                   ? GetRecursiveChildren(user, filter)
                   : GetChildren(user, true).Where(filter);
            }

            return PostFilterAndSort(items, query);
        }

        protected QueryResult<BaseItem> PostFilterAndSort(IEnumerable<BaseItem> items, InternalItemsQuery query)
        {
            return UserViewBuilder.PostFilterAndSort(items, this, null, query, LibraryManager);
        }

        public virtual IEnumerable<BaseItem> GetChildren(User user, bool includeLinkedChildren)
        {
            if (user == null)
            {
                throw new ArgumentNullException();
            }

            //the true root should return our users root folder children
            if (IsPhysicalRoot) return user.RootFolder.GetChildren(user, includeLinkedChildren);

            var result = new Dictionary<Guid, BaseItem>();

            AddChildren(user, includeLinkedChildren, result, false, null);

            return result.Values;
        }

        protected virtual IEnumerable<BaseItem> GetEligibleChildrenForRecursiveChildren(User user)
        {
            return Children;
        }

        /// <summary>
        /// Adds the children to list.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="includeLinkedChildren">if set to <c>true</c> [include linked children].</param>
        /// <param name="result">The result.</param>
        /// <param name="recursive">if set to <c>true</c> [recursive].</param>
        /// <param name="filter">The filter.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise</returns>
        private void AddChildren(User user, bool includeLinkedChildren, Dictionary<Guid, BaseItem> result, bool recursive, Func<BaseItem, bool> filter)
        {
            foreach (var child in GetEligibleChildrenForRecursiveChildren(user))
            {
                if (child.IsVisible(user))
                {
                    if (filter == null || filter(child))
                    {
                        result[child.Id] = child;
                    }

                    if (recursive && child.IsFolder)
                    {
                        var folder = (Folder)child;

                        folder.AddChildren(user, includeLinkedChildren, result, true, filter);
                    }
                }
            }

            if (includeLinkedChildren)
            {
                foreach (var child in GetLinkedChildren(user))
                {
                    if (child.IsVisible(user))
                    {
                        if (filter == null || filter(child))
                        {
                            result[child.Id] = child;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gets allowed recursive children of an item
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="includeLinkedChildren">if set to <c>true</c> [include linked children].</param>
        /// <returns>IEnumerable{BaseItem}.</returns>
        /// <exception cref="System.ArgumentNullException"></exception>
        public IEnumerable<BaseItem> GetRecursiveChildren(User user, bool includeLinkedChildren = true)
        {
            return GetRecursiveChildren(user, i => true);
        }

        public virtual IEnumerable<BaseItem> GetRecursiveChildren(User user, Func<BaseItem, bool> filter)
        {
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            var result = new Dictionary<Guid, BaseItem>();

            AddChildren(user, true, result, true, filter);

            return result.Values;
        }

        /// <summary>
        /// Gets the recursive children.
        /// </summary>
        /// <returns>IList{BaseItem}.</returns>
        public IList<BaseItem> GetRecursiveChildren()
        {
            return GetRecursiveChildren(i => true);
        }

        public IList<BaseItem> GetRecursiveChildren(Func<BaseItem, bool> filter)
        {
            var list = new List<BaseItem>();

            AddChildrenToList(list, true, filter);

            return list;
        }

        /// <summary>
        /// Adds the children to list.
        /// </summary>
        /// <param name="list">The list.</param>
        /// <param name="recursive">if set to <c>true</c> [recursive].</param>
        /// <param name="filter">The filter.</param>
        private void AddChildrenToList(List<BaseItem> list, bool recursive, Func<BaseItem, bool> filter)
        {
            foreach (var child in Children)
            {
                if (filter == null || filter(child))
                {
                    list.Add(child);
                }

                if (recursive && child.IsFolder)
                {
                    var folder = (Folder)child;

                    folder.AddChildrenToList(list, true, filter);
                }
            }
        }


        /// <summary>
        /// Gets the linked children.
        /// </summary>
        /// <returns>IEnumerable{BaseItem}.</returns>
        public IEnumerable<BaseItem> GetLinkedChildren()
        {
            return LinkedChildren
                .Select(GetLinkedChild)
                .Where(i => i != null);
        }

        protected virtual bool FilterLinkedChildrenPerUser
        {
            get
            {
                return false;
            }
        }

        public IEnumerable<BaseItem> GetLinkedChildren(User user)
        {
            if (!FilterLinkedChildrenPerUser || user == null)
            {
                return GetLinkedChildren();
            }

            var locations = user.RootFolder
                .Children
                .OfType<CollectionFolder>()
                .Where(i => i.IsVisible(user))
                .SelectMany(i => i.PhysicalLocations)
                .ToList();

            return LinkedChildren
                .Select(i =>
                {
                    var requiresPostFilter = true;

                    if (!string.IsNullOrWhiteSpace(i.Path))
                    {
                        requiresPostFilter = false;

                        if (!locations.Any(l => FileSystem.ContainsSubPath(l, i.Path)))
                        {
                            return null;
                        }
                    }

                    var child = GetLinkedChild(i);

                    if (requiresPostFilter && child != null)
                    {
                        if (string.IsNullOrWhiteSpace(child.Path))
                        {
                            Logger.Debug("Found LinkedChild with null path: {0}", child.Name);
                            return child;
                        }

                        if (!locations.Any(l => FileSystem.ContainsSubPath(l, child.Path)))
                        {
                            return null;
                        }
                    }

                    return child;
                })
                .Where(i => i != null);
        }

        /// <summary>
        /// Gets the linked children.
        /// </summary>
        /// <returns>IEnumerable{BaseItem}.</returns>
        public IEnumerable<Tuple<LinkedChild, BaseItem>> GetLinkedChildrenInfos()
        {
            return LinkedChildren
                .Select(i => new Tuple<LinkedChild, BaseItem>(i, GetLinkedChild(i)))
                .Where(i => i.Item2 != null);
        }

        [IgnoreDataMember]
        protected override bool SupportsOwnedItems
        {
            get
            {
                return base.SupportsOwnedItems || SupportsShortcutChildren;
            }
        }

        protected override async Task<bool> RefreshedOwnedItems(MetadataRefreshOptions options, List<FileSystemMetadata> fileSystemChildren, CancellationToken cancellationToken)
        {
            var changesFound = false;

            if (LocationType == LocationType.FileSystem)
            {
                if (RefreshLinkedChildren(fileSystemChildren))
                {
                    changesFound = true;
                }
            }

            var baseHasChanges = await base.RefreshedOwnedItems(options, fileSystemChildren, cancellationToken).ConfigureAwait(false);

            return baseHasChanges || changesFound;
        }

        /// <summary>
        /// Refreshes the linked children.
        /// </summary>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise</returns>
        private bool RefreshLinkedChildren(IEnumerable<FileSystemMetadata> fileSystemChildren)
        {
            var currentManualLinks = LinkedChildren.Where(i => i.Type == LinkedChildType.Manual).ToList();
            var currentShortcutLinks = LinkedChildren.Where(i => i.Type == LinkedChildType.Shortcut).ToList();

            List<LinkedChild> newShortcutLinks;

            if (SupportsShortcutChildren)
            {
                newShortcutLinks = fileSystemChildren
                    .Where(i => (i.Attributes & FileAttributes.Directory) != FileAttributes.Directory && FileSystem.IsShortcut(i.FullName))
                    .Select(i =>
                    {
                        try
                        {
                            Logger.Debug("Found shortcut at {0}", i.FullName);

                            var resolvedPath = FileSystem.ResolveShortcut(i.FullName);

                            if (!string.IsNullOrEmpty(resolvedPath))
                            {
                                return new LinkedChild
                                {
                                    Path = resolvedPath,
                                    Type = LinkedChildType.Shortcut
                                };
                            }

                            Logger.Error("Error resolving shortcut {0}", i.FullName);

                            return null;
                        }
                        catch (IOException ex)
                        {
                            Logger.ErrorException("Error resolving shortcut {0}", ex, i.FullName);
                            return null;
                        }
                    })
                    .Where(i => i != null)
                    .ToList();
            }
            else { newShortcutLinks = new List<LinkedChild>(); }

            if (!newShortcutLinks.SequenceEqual(currentShortcutLinks, new LinkedChildComparer()))
            {
                Logger.Info("Shortcut links have changed for {0}", Path);

                newShortcutLinks.AddRange(currentManualLinks);
                LinkedChildren = newShortcutLinks;
                return true;
            }

            foreach (var child in LinkedChildren)
            {
                // Reset the cached value
                child.ItemId = null;
            }

            return false;
        }

        /// <summary>
        /// Folders need to validate and refresh
        /// </summary>
        /// <returns>Task.</returns>
        public override async Task ChangedExternally()
        {
            var progress = new Progress<double>();

            await ValidateChildren(progress, CancellationToken.None).ConfigureAwait(false);

            await base.ChangedExternally().ConfigureAwait(false);
        }

        /// <summary>
        /// Marks the played.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="datePlayed">The date played.</param>
        /// <param name="resetPosition">if set to <c>true</c> [reset position].</param>
        /// <returns>Task.</returns>
        public override async Task MarkPlayed(User user,
            DateTime? datePlayed,
            bool resetPosition)
        {
            var query = new InternalItemsQuery
            {
                User = user,
                Recursive = true,
                IsFolder = false,
                IsUnaired = false

            };

            if (!user.Configuration.DisplayMissingEpisodes)
            {
                query.IsMissing = false;
            }

            var itemsResult = await GetItems(query).ConfigureAwait(false);

            // Sweep through recursively and update status
            var tasks = itemsResult.Items.Select(c => c.MarkPlayed(user, datePlayed, resetPosition));

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// Marks the unplayed.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <returns>Task.</returns>
        public override async Task MarkUnplayed(User user)
        {
            var itemsResult = await GetItems(new InternalItemsQuery
            {
                User = user,
                Recursive = true,
                IsFolder = false

            }).ConfigureAwait(false);

            // Sweep through recursively and update status
            var tasks = itemsResult.Items.Select(c => c.MarkUnplayed(user));

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// Finds an item by path, recursively
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>BaseItem.</returns>
        /// <exception cref="System.ArgumentNullException"></exception>
        public BaseItem FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException();
            }

            if (string.Equals(Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return this;
            }

            if (PhysicalLocations.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                return this;
            }

            return GetRecursiveChildren(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase) ||
                (!i.IsFolder && !i.IsInMixedFolder && string.Equals(i.ContainingFolderPath, path, StringComparison.OrdinalIgnoreCase)) ||
                i.PhysicalLocations.Contains(path, StringComparer.OrdinalIgnoreCase))
                .FirstOrDefault();
        }

        public override bool IsPlayed(User user)
        {
            return GetRecursiveChildren(user, i => !i.IsFolder && i.LocationType != LocationType.Virtual)
                .All(i => i.IsPlayed(user));
        }

        public override bool IsUnplayed(User user)
        {
            return !IsPlayed(user);
        }

        public override void FillUserDataDtoValues(UserItemDataDto dto, UserItemData userData, User user)
        {
            var recursiveItemCount = 0;
            var unplayed = 0;

            double totalPercentPlayed = 0;

            IEnumerable<BaseItem> children;
            var folder = this;

            var season = folder as Season;

            if (season != null)
            {
                children = season.GetEpisodes(user).Where(i => i.LocationType != LocationType.Virtual);
            }
            else
            {
                children = folder.GetRecursiveChildren(user, i => !i.IsFolder && i.LocationType != LocationType.Virtual);
            }

            // Loop through each recursive child
            foreach (var child in children)
            {
                recursiveItemCount++;

                var isUnplayed = true;

                var itemUserData = UserDataManager.GetUserData(user.Id, child.GetUserDataKey());

                // Incrememt totalPercentPlayed
                if (itemUserData != null)
                {
                    if (itemUserData.Played)
                    {
                        totalPercentPlayed += 100;

                        isUnplayed = false;
                    }
                    else if (itemUserData.PlaybackPositionTicks > 0 && child.RunTimeTicks.HasValue && child.RunTimeTicks.Value > 0)
                    {
                        double itemPercent = itemUserData.PlaybackPositionTicks;
                        itemPercent /= child.RunTimeTicks.Value;
                        totalPercentPlayed += itemPercent;
                    }
                }

                if (isUnplayed)
                {
                    unplayed++;
                }
            }

            dto.UnplayedItemCount = unplayed;

            if (recursiveItemCount > 0)
            {
                dto.PlayedPercentage = totalPercentPlayed / recursiveItemCount;
                dto.Played = dto.PlayedPercentage.Value >= 100;
            }
        }
    }
}