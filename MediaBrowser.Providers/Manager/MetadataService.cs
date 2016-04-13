﻿using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.IO;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommonIO;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Providers;

namespace MediaBrowser.Providers.Manager
{
    public abstract class MetadataService<TItemType, TIdType> : IMetadataService
        where TItemType : IHasMetadata, IHasLookupInfo<TIdType>, new()
        where TIdType : ItemLookupInfo, new()
    {
        protected readonly IServerConfigurationManager ServerConfigurationManager;
        protected readonly ILogger Logger;
        protected readonly IProviderManager ProviderManager;
        protected readonly IProviderRepository ProviderRepo;
        protected readonly IFileSystem FileSystem;
        protected readonly IUserDataManager UserDataManager;
        protected readonly ILibraryManager LibraryManager;

        protected MetadataService(IServerConfigurationManager serverConfigurationManager, ILogger logger, IProviderManager providerManager, IProviderRepository providerRepo, IFileSystem fileSystem, IUserDataManager userDataManager, ILibraryManager libraryManager)
        {
            ServerConfigurationManager = serverConfigurationManager;
            Logger = logger;
            ProviderManager = providerManager;
            ProviderRepo = providerRepo;
            FileSystem = fileSystem;
            UserDataManager = userDataManager;
            LibraryManager = libraryManager;
        }

        /// <summary>
        /// Saves the provider result.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="result">The result.</param>
        /// <param name="directoryService">The directory service.</param>
        /// <returns>Task.</returns>
        protected Task SaveProviderResult(TItemType item, MetadataStatus result, IDirectoryService directoryService)
        {
            result.ItemId = item.Id;

            //var locationType = item.LocationType;

            //if (locationType == LocationType.FileSystem || locationType == LocationType.Offline)
            //{
            //    if (!string.IsNullOrWhiteSpace(item.Path))
            //    {
            //        var file = directoryService.GetFile(item.Path);

            //        if ((file.Attributes & FileAttributes.Directory) != FileAttributes.Directory && file.Exists)
            //        {
            //            result.ItemDateModified = FileSystem.GetLastWriteTimeUtc(file);
            //        }
            //    }
            //}

            result.ItemDateModified = item.DateModified;

            if (EnableDateLastRefreshed(item))
            {
                return Task.FromResult(true);
            }

            return ProviderRepo.SaveMetadataStatus(result, CancellationToken.None);
        }

        /// <summary>
        /// Gets the last result.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns>ProviderResult.</returns>
        protected MetadataStatus GetLastResult(IHasMetadata item)
        {
            if (GetLastRefreshDate(item) == default(DateTime))
            {
                return new MetadataStatus { ItemId = item.Id };
            }

            if (EnableDateLastRefreshed(item) && item.DateModifiedDuringLastRefresh.HasValue)
            {
                return new MetadataStatus
                {
                    ItemId = item.Id,
                    DateLastImagesRefresh = item.DateLastRefreshed,
                    DateLastMetadataRefresh = item.DateLastRefreshed,
                    ItemDateModified = item.DateModifiedDuringLastRefresh.Value
                };
            }

            var result = ProviderRepo.GetMetadataStatus(item.Id) ?? new MetadataStatus { ItemId = item.Id };

            item.DateModifiedDuringLastRefresh = result.ItemDateModified;

            return result;
        }

        public async Task<ItemUpdateType> RefreshMetadata(IHasMetadata item, MetadataRefreshOptions refreshOptions, CancellationToken cancellationToken)
        {
            var itemOfType = (TItemType)item;
            var config = ProviderManager.GetMetadataOptions(item);

            var updateType = ItemUpdateType.None;
            var refreshResult = GetLastResult(item);

            var itemImageProvider = new ItemImageProvider(Logger, ProviderManager, ServerConfigurationManager, FileSystem);
            var localImagesFailed = false;

            var allImageProviders = ((ProviderManager)ProviderManager).GetImageProviders(item, refreshOptions).ToList();

            // Start by validating images
            try
            {
                // Always validate images and check for new locally stored ones.
                if (itemImageProvider.ValidateImages(item, allImageProviders.OfType<ILocalImageProvider>(), refreshOptions.DirectoryService))
                {
                    updateType = updateType | ItemUpdateType.ImageUpdate;
                }
            }
            catch (Exception ex)
            {
                localImagesFailed = true;
                Logger.ErrorException("Error validating images for {0}", ex, item.Path ?? item.Name ?? "Unknown name");
            }

            var metadataResult = new MetadataResult<TItemType>
            {
                Item = itemOfType
            };

            bool hasRefreshedMetadata = false;
            bool hasRefreshedImages = false;

            // Next run metadata providers
            if (refreshOptions.MetadataRefreshMode != MetadataRefreshMode.None)
            {
                var providers = GetProviders(item, refreshResult, refreshOptions)
                    .ToList();

                var dateLastRefresh = EnableDateLastRefreshed(item)
                     ? item.DateLastRefreshed
                     : refreshResult.DateLastMetadataRefresh ?? default(DateTime);

                if (providers.Count > 0 || dateLastRefresh == default(DateTime))
                {
                    if (item.BeforeMetadataRefresh())
                    {
                        updateType = updateType | ItemUpdateType.MetadataImport;
                    }
                }

                if (providers.Count > 0)
                {
                    var id = itemOfType.GetLookupInfo();

                    if (refreshOptions.SearchResult != null)
                    {
                        ApplySearchResult(id, refreshOptions.SearchResult);
                    }

                    //await FindIdentities(id, cancellationToken).ConfigureAwait(false);
                    id.IsAutomated = refreshOptions.IsAutomated;

                    var result = await RefreshWithProviders(metadataResult, id, refreshOptions, providers, itemImageProvider, cancellationToken).ConfigureAwait(false);

                    updateType = updateType | result.UpdateType;
                    if (result.Failures == 0)
                    {
                        refreshResult.SetDateLastMetadataRefresh(DateTime.UtcNow);
                        hasRefreshedMetadata = true;
                    }
                    else
                    {
                        refreshResult.SetDateLastMetadataRefresh(null);
                    }
                }
            }

            // Next run remote image providers, but only if local image providers didn't throw an exception
            if (!localImagesFailed && refreshOptions.ImageRefreshMode != ImageRefreshMode.ValidationOnly)
            {
                var providers = GetNonLocalImageProviders(item, allImageProviders, refreshResult, refreshOptions).ToList();

                if (providers.Count > 0)
                {
                    var result = await itemImageProvider.RefreshImages(itemOfType, providers, refreshOptions, config, cancellationToken).ConfigureAwait(false);

                    updateType = updateType | result.UpdateType;
                    if (result.Failures == 0)
                    {
                        refreshResult.SetDateLastImagesRefresh(DateTime.UtcNow);
                        hasRefreshedImages = true;
                    }
                    else
                    {
                        refreshResult.SetDateLastImagesRefresh(null);
                    }
                }
            }

            var isFirstRefresh = GetLastRefreshDate(item) == default(DateTime);

            var beforeSaveResult = await BeforeSave(itemOfType, isFirstRefresh || refreshOptions.ReplaceAllMetadata || refreshOptions.MetadataRefreshMode == MetadataRefreshMode.FullRefresh, updateType).ConfigureAwait(false);
            updateType = updateType | beforeSaveResult;

            // Save if changes were made, or it's never been saved before
            if (refreshOptions.ForceSave || updateType > ItemUpdateType.None || isFirstRefresh || refreshOptions.ReplaceAllMetadata)
            {
                // If any of these properties are set then make sure the updateType is not None, just to force everything to save
                if (refreshOptions.ForceSave || refreshOptions.ReplaceAllMetadata)
                {
                    updateType = updateType | ItemUpdateType.MetadataDownload;
                }

                if (hasRefreshedMetadata && hasRefreshedImages)
                {
                    item.DateLastRefreshed = DateTime.UtcNow;
                    item.DateModifiedDuringLastRefresh = item.DateModified;
                }
                else
                {
                    item.DateLastRefreshed = default(DateTime);
                    item.DateModifiedDuringLastRefresh = null;
                }

                // Save to database
                await SaveItem(metadataResult, updateType, cancellationToken).ConfigureAwait(false);
            }

            if (updateType > ItemUpdateType.None || refreshResult.IsDirty)
            {
                await SaveProviderResult(itemOfType, refreshResult, refreshOptions.DirectoryService).ConfigureAwait(false);
            }

            await AfterMetadataRefresh(itemOfType, refreshOptions, cancellationToken).ConfigureAwait(false);

            return updateType;
        }

        private void ApplySearchResult(ItemLookupInfo lookupInfo, RemoteSearchResult result)
        {
            lookupInfo.ProviderIds = result.ProviderIds;
            lookupInfo.Name = result.Name;
            lookupInfo.Year = result.ProductionYear;
        }

        private async Task FindIdentities(TIdType id, CancellationToken cancellationToken)
        {
            try
            {
                await ItemIdentifier<TIdType>.FindIdentities(id, ProviderManager, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.ErrorException("Error in FindIdentities", ex);
            }
        }

        private DateTime GetLastRefreshDate(IHasMetadata item)
        {
            if (EnableDateLastRefreshed(item))
            {
                return item.DateLastRefreshed;
            }

            return item.DateLastSaved;
        }

        private bool EnableDateLastRefreshed(IHasMetadata item)
        {
            if (ServerConfigurationManager.Configuration.EnableDateLastRefresh)
            {
                return true;
            }

            if (item.DateLastRefreshed != default(DateTime))
            {
                return true;
            }

            if (item is BoxSet || item is IItemByName || item is Playlist)
            {
                return true;
            }

            if (item.SourceType != SourceType.Library)
            {
                return true;
            }

            return false;
        }

        protected async Task SaveItem(MetadataResult<TItemType> result, ItemUpdateType reason, CancellationToken cancellationToken)
        {
            if (result.Item.SupportsPeople && result.People != null)
            {
                await LibraryManager.UpdatePeople(result.Item as BaseItem, result.People.ToList());
                await SavePeopleMetadata(result.People, cancellationToken).ConfigureAwait(false);
            }
            await result.Item.UpdateToRepository(reason, cancellationToken).ConfigureAwait(false);
        }

        private async Task SavePeopleMetadata(List<PersonInfo> people, CancellationToken cancellationToken)
        {
            foreach (var person in people)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (person.ProviderIds.Any() || !string.IsNullOrWhiteSpace(person.ImageUrl))
                {
                    var updateType = ItemUpdateType.MetadataDownload;

                    var saveEntity = false;
                    var personEntity = LibraryManager.GetPerson(person.Name);
                    foreach (var id in person.ProviderIds)
                    {
                        if (!string.Equals(personEntity.GetProviderId(id.Key), id.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            personEntity.SetProviderId(id.Key, id.Value);
                            saveEntity = true;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(person.ImageUrl) && !personEntity.HasImage(ImageType.Primary))
                    {
                        await AddPersonImage(personEntity, person.ImageUrl, cancellationToken).ConfigureAwait(false);

                        saveEntity = true;
                        updateType = updateType | ItemUpdateType.ImageUpdate;
                    }

                    if (saveEntity)
                    {
                        await personEntity.UpdateToRepository(updateType, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task AddPersonImage(Person personEntity, string imageUrl, CancellationToken cancellationToken)
        {
            if (ServerConfigurationManager.Configuration.DownloadImagesInAdvance)
            {
                try
                {
                    await ProviderManager.SaveImage(personEntity, imageUrl, null, ImageType.Primary, null, cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex)
                {
                    Logger.ErrorException("Error in AddPersonImage", ex);
                }
            }

            personEntity.SetImage(new ItemImageInfo
            {
                Path = imageUrl,
                Type = ImageType.Primary,
                IsPlaceholder = true
            }, 0);
        }

        private readonly Task _cachedTask = Task.FromResult(true);
        protected virtual Task AfterMetadataRefresh(TItemType item, MetadataRefreshOptions refreshOptions, CancellationToken cancellationToken)
        {
            item.AfterMetadataRefresh();
            return _cachedTask;
        }

        private readonly Task<ItemUpdateType> _cachedResult = Task.FromResult(ItemUpdateType.None);
        /// <summary>
        /// Befores the save.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="isFullRefresh">if set to <c>true</c> [is full refresh].</param>
        /// <param name="currentUpdateType">Type of the current update.</param>
        /// <returns>ItemUpdateType.</returns>
        protected virtual Task<ItemUpdateType> BeforeSave(TItemType item, bool isFullRefresh, ItemUpdateType currentUpdateType)
        {
            return _cachedResult;
        }

        /// <summary>
        /// Gets the providers.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="status">The status.</param>
        /// <param name="options">The options.</param>
        /// <returns>IEnumerable{`0}.</returns>
        protected IEnumerable<IMetadataProvider> GetProviders(IHasMetadata item, MetadataStatus status, MetadataRefreshOptions options)
        {
            // Get providers to refresh
            var providers = ((ProviderManager)ProviderManager).GetMetadataProviders<TItemType>(item).ToList();

            var dateLastRefresh = EnableDateLastRefreshed(item)
                ? item.DateLastRefreshed
                : status.DateLastMetadataRefresh ?? default(DateTime);

            // Run all if either of these flags are true
            var runAllProviders = options.ReplaceAllMetadata || options.MetadataRefreshMode == MetadataRefreshMode.FullRefresh || dateLastRefresh == default(DateTime);

            if (!runAllProviders)
            {
                // Avoid implicitly captured closure
                var currentItem = item;

                var providersWithChanges = providers
                    .Where(i =>
                    {
                        var hasChangeMonitor = i as IHasChangeMonitor;
                        if (hasChangeMonitor != null)
                        {
                            return HasChanged(item, hasChangeMonitor, currentItem.DateLastSaved, options.DirectoryService);
                        }

                        var hasFileChangeMonitor = i as IHasItemChangeMonitor;
                        if (hasFileChangeMonitor != null)
                        {
                            return HasChanged(item, hasFileChangeMonitor, options.DirectoryService);
                        }

                        return false;
                    })
                    .ToList();

                if (providersWithChanges.Count == 0)
                {
                    providers = new List<IMetadataProvider<TItemType>>();
                }
                else
                {
                    providers = providers.Where(i =>
                    {
                        // If any provider reports a change, always run local ones as well
                        if (i is ILocalMetadataProvider)
                        {
                            return true;
                        }

                        var anyRemoteProvidersChanged = providersWithChanges.OfType<IRemoteMetadataProvider>()
                            .Any();

                        // If any remote providers changed, run them all so that priorities can be honored
                        if (i is IRemoteMetadataProvider)
                        {
                            return anyRemoteProvidersChanged;
                        }

                        // Run custom providers if they report a change or any remote providers change
                        return anyRemoteProvidersChanged || providersWithChanges.Contains(i);

                    }).ToList();
                }
            }

            return providers;
        }

        protected virtual IEnumerable<IImageProvider> GetNonLocalImageProviders(IHasMetadata item, IEnumerable<IImageProvider> allImageProviders, MetadataStatus status, ImageRefreshOptions options)
        {
            // Get providers to refresh
            var providers = allImageProviders.Where(i => !(i is ILocalImageProvider)).ToList();

            var dateLastImageRefresh = EnableDateLastRefreshed(item)
                  ? item.DateLastRefreshed
                  : status.DateLastImagesRefresh ?? default(DateTime);

            // Run all if either of these flags are true
            var runAllProviders = options.ImageRefreshMode == ImageRefreshMode.FullRefresh || dateLastImageRefresh == default(DateTime);

            if (!runAllProviders)
            {
                providers = providers
                    .Where(i =>
                    {
                        var hasChangeMonitor = i as IHasChangeMonitor;
                        if (hasChangeMonitor != null)
                        {
                            return HasChanged(item, hasChangeMonitor, dateLastImageRefresh, options.DirectoryService);
                        }

                        var hasFileChangeMonitor = i as IHasItemChangeMonitor;
                        if (hasFileChangeMonitor != null)
                        {
                            return HasChanged(item, hasFileChangeMonitor, options.DirectoryService);
                        }

                        return false;
                    })
                    .ToList();
            }

            return providers;
        }

        public bool CanRefresh(IHasMetadata item)
        {
            return item is TItemType;
        }

        protected virtual async Task<RefreshResult> RefreshWithProviders(MetadataResult<TItemType> metadata,
            TIdType id,
            MetadataRefreshOptions options,
            List<IMetadataProvider> providers,
            ItemImageProvider imageService,
            CancellationToken cancellationToken)
        {
            var refreshResult = new RefreshResult
            {
                UpdateType = ItemUpdateType.None,
                Providers = providers.Select(i => i.GetType().FullName.GetMD5()).ToList()
            };

            var item = metadata.Item;

            var customProviders = providers.OfType<ICustomMetadataProvider<TItemType>>().ToList();
            var logName = item.LocationType == LocationType.Remote ? item.Name ?? item.Path : item.Path ?? item.Name;

            foreach (var provider in customProviders.Where(i => i is IPreRefreshProvider))
            {
                await RunCustomProvider(provider, item, logName, options, refreshResult, cancellationToken).ConfigureAwait(false);
            }

            var temp = new MetadataResult<TItemType>
            {
                Item = CreateNew()
            };
            temp.Item.Path = item.Path;

            var userDataList = new List<UserItemData>();

            // If replacing all metadata, run internet providers first
            if (options.ReplaceAllMetadata)
            {
                var remoteResult = await ExecuteRemoteProviders(temp, logName, id, providers.OfType<IRemoteMetadataProvider<TItemType, TIdType>>(), cancellationToken)
                    .ConfigureAwait(false);

                refreshResult.UpdateType = refreshResult.UpdateType | remoteResult.UpdateType;
                refreshResult.ErrorMessage = remoteResult.ErrorMessage;
                refreshResult.Failures += remoteResult.Failures;
            }

            var hasLocalMetadata = false;

            foreach (var provider in providers.OfType<ILocalMetadataProvider<TItemType>>().ToList())
            {
                var providerName = provider.GetType().Name;
                Logger.Debug("Running {0} for {1}", providerName, logName);

                var itemInfo = new ItemInfo(item);

                try
                {
                    var localItem = await provider.GetMetadata(itemInfo, options.DirectoryService, cancellationToken).ConfigureAwait(false);

                    if (localItem.HasMetadata)
                    {
                        if (imageService.MergeImages(item, localItem.Images))
                        {
                            refreshResult.UpdateType = refreshResult.UpdateType | ItemUpdateType.ImageUpdate;
                        }

                        if (localItem.UserDataList != null)
                        {
                            userDataList.AddRange(localItem.UserDataList);
                        }

                        MergeData(localItem, temp, new List<MetadataFields>(), !options.ReplaceAllMetadata, true);
                        refreshResult.UpdateType = refreshResult.UpdateType | ItemUpdateType.MetadataImport;

                        // Only one local provider allowed per item
                        if (IsFullLocalMetadata(localItem.Item))
                        {
                            hasLocalMetadata = true;
                        }
                        break;
                    }

                    Logger.Debug("{0} returned no metadata for {1}", providerName, logName);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    refreshResult.Failures++;

                    Logger.ErrorException("Error in {0}", ex, provider.Name);

                    // If a local provider fails, consider that a failure
                    refreshResult.ErrorMessage = ex.Message;

                    if (options.MetadataRefreshMode != MetadataRefreshMode.FullRefresh)
                    {
                        // If the local provider fails don't continue with remote providers because the user's saved metadata could be lost
                        //return refreshResult;
                    }
                }
            }

            // Local metadata is king - if any is found don't run remote providers
            if (!options.ReplaceAllMetadata && (!hasLocalMetadata || options.MetadataRefreshMode == MetadataRefreshMode.FullRefresh))
            {
                var remoteResult = await ExecuteRemoteProviders(temp, logName, id, providers.OfType<IRemoteMetadataProvider<TItemType, TIdType>>(), cancellationToken)
                    .ConfigureAwait(false);

                refreshResult.UpdateType = refreshResult.UpdateType | remoteResult.UpdateType;
                refreshResult.ErrorMessage = remoteResult.ErrorMessage;
                refreshResult.Failures += remoteResult.Failures;
            }

            if (providers.Any(i => !(i is ICustomMetadataProvider)))
            {
                if (refreshResult.UpdateType > ItemUpdateType.None)
                {
                    // If no local metadata, take data from item itself
                    if (!hasLocalMetadata)
                    {
                        // TODO: If the new metadata from above has some blank data, this can cause old data to get filled into those empty fields
                        MergeData(metadata, temp, new List<MetadataFields>(), false, true);
                    }

                    MergeData(temp, metadata, item.LockedFields, true, true);
                }
            }

            //var isUnidentified = failedProviderCount > 0 && successfulProviderCount == 0;

            foreach (var provider in customProviders.Where(i => !(i is IPreRefreshProvider)))
            {
                await RunCustomProvider(provider, item, logName, options, refreshResult, cancellationToken).ConfigureAwait(false);
            }

            await ImportUserData(item, userDataList, cancellationToken).ConfigureAwait(false);

            return refreshResult;
        }

        protected virtual bool IsFullLocalMetadata(TItemType item)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                return false;
            }

            return true;
        }

        private async Task ImportUserData(TItemType item, List<UserItemData> userDataList, CancellationToken cancellationToken)
        {
            var hasUserData = item as IHasUserData;

            if (hasUserData != null)
            {
                foreach (var userData in userDataList)
                {
                    await UserDataManager.SaveUserData(userData.UserId, hasUserData, userData, UserDataSaveReason.Import, cancellationToken)
                            .ConfigureAwait(false);
                }
            }
        }

        private async Task RunCustomProvider(ICustomMetadataProvider<TItemType> provider, TItemType item, string logName, MetadataRefreshOptions options, RefreshResult refreshResult, CancellationToken cancellationToken)
        {
            Logger.Debug("Running {0} for {1}", provider.GetType().Name, logName);

            try
            {
                refreshResult.UpdateType = refreshResult.UpdateType | await provider.FetchAsync(item, options, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                refreshResult.ErrorMessage = ex.Message;
                Logger.ErrorException("Error in {0}", ex, provider.Name);
            }
        }

        protected virtual TItemType CreateNew()
        {
            return new TItemType();
        }

        private async Task<RefreshResult> ExecuteRemoteProviders(MetadataResult<TItemType> temp, string logName, TIdType id, IEnumerable<IRemoteMetadataProvider<TItemType, TIdType>> providers, CancellationToken cancellationToken)
        {
            var refreshResult = new RefreshResult();

            foreach (var provider in providers)
            {
                var providerName = provider.GetType().Name;
                Logger.Debug("Running {0} for {1}", providerName, logName);

                if (id != null)
                {
                    MergeNewData(temp.Item, id);
                }

                try
                {
                    var result = await provider.GetMetadata(id, cancellationToken).ConfigureAwait(false);

                    if (result.HasMetadata)
                    {
                        NormalizeRemoteResult(result.Item);

                        MergeData(result, temp, new List<MetadataFields>(), false, false);

                        refreshResult.UpdateType = refreshResult.UpdateType | ItemUpdateType.MetadataDownload;
                    }
                    else
                    {
                        refreshResult.Failures++;
                        Logger.Debug("{0} returned no metadata for {1}", providerName, logName);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    refreshResult.Failures++;
                    refreshResult.ErrorMessage = ex.Message;
                    Logger.ErrorException("Error in {0}", ex, provider.Name);
                }
            }

            return refreshResult;
        }

        private void NormalizeRemoteResult(TItemType item)
        {
            if (!ServerConfigurationManager.Configuration.FindInternetTrailers)
            {
                var hasTrailers = item as IHasTrailers;

                if (hasTrailers != null)
                {
                    hasTrailers.RemoteTrailers.Clear();
                }
            }
        }

        private void MergeNewData(TItemType source, TIdType lookupInfo)
        {
            // Copy new provider id's that may have been obtained
            foreach (var providerId in source.ProviderIds)
            {
                var key = providerId.Key;

                // Don't replace existing Id's.
                if (!lookupInfo.ProviderIds.ContainsKey(key))
                {
                    lookupInfo.ProviderIds[key] = providerId.Value;
                }
            }
        }

        protected abstract void MergeData(MetadataResult<TItemType> source,
            MetadataResult<TItemType> target,
            List<MetadataFields> lockedFields,
            bool replaceData,
            bool mergeMetadataSettings);

        public virtual int Order
        {
            get
            {
                return 0;
            }
        }

        private bool HasChanged(IHasMetadata item, IHasItemChangeMonitor changeMonitor, IDirectoryService directoryService)
        {
            try
            {
                var hasChanged = changeMonitor.HasChanged(item, directoryService);

                //if (hasChanged)
                //{
                //    Logger.Debug("{0} reports change to {1}", changeMonitor.GetType().Name, item.Path ?? item.Name);
                //}

                return hasChanged;
            }
            catch (Exception ex)
            {
                Logger.ErrorException("Error in {0}.HasChanged", ex, changeMonitor.GetType().Name);
                return false;
            }
        }

        private bool HasChanged(IHasMetadata item, IHasChangeMonitor changeMonitor, DateTime date, IDirectoryService directoryService)
        {
            try
            {
                var hasChanged = changeMonitor.HasChanged(item, directoryService, date);

                //if (hasChanged)
                //{
                //    Logger.Debug("{0} reports change to {1} since {2}", changeMonitor.GetType().Name,
                //        item.Path ?? item.Name, date);
                //}

                return hasChanged;
            }
            catch (Exception ex)
            {
                Logger.ErrorException("Error in {0}.HasChanged", ex, changeMonitor.GetType().Name);
                return false;
            }
        }
    }

    public class RefreshResult
    {
        public ItemUpdateType UpdateType { get; set; }
        public string ErrorMessage { get; set; }
        public List<Guid> Providers { get; set; }
        public int Failures { get; set; }
    }
}
