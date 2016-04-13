﻿using System.Collections.Generic;

namespace MediaBrowser.Model.LiveTv
{
    public class LiveTvOptions
    {
        public int? GuideDays { get; set; }
        public bool EnableMovieProviders { get; set; }
        public string RecordingPath { get; set; }
        public bool EnableAutoOrganize { get; set; }
        public bool EnableRecordingEncoding { get; set; }

        public List<TunerHostInfo> TunerHosts { get; set; }
        public List<ListingsProviderInfo> ListingProviders { get; set; }

        public int PrePaddingSeconds { get; set; }
        public int PostPaddingSeconds { get; set; }

        public LiveTvOptions()
        {
            EnableMovieProviders = true;
            TunerHosts = new List<TunerHostInfo>();
            ListingProviders = new List<ListingsProviderInfo>();
        }
    }

    public class TunerHostInfo
    {
        public string Id { get; set; }
        public string Url { get; set; }
        public string Type { get; set; }
        public string DeviceId { get; set; }
        public bool ImportFavoritesOnly { get; set; }
        public bool AllowHWTranscoding { get; set; }
        public bool IsEnabled { get; set; }
        public string M3UUrl { get; set; }
        public string InfoUrl { get; set; }
        public string FriendlyName { get; set; }
        public int Tuners { get; set; }
        public string DiseqC { get; set; }
        public string SourceA { get; set; }
        public string SourceB { get; set; }
        public string SourceC { get; set; }
        public string SourceD { get; set; }

        public int DataVersion { get; set; }

        public TunerHostInfo()
        {
            IsEnabled = true;
            AllowHWTranscoding = true;
        }
    }

    public class ListingsProviderInfo
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string ListingsId { get; set; }
        public string ZipCode { get; set; }
        public string Country { get; set; }
        public string Path { get; set; }

        public string[] EnabledTuners { get; set; }
        public bool EnableAllTuners { get; set; }

        public ListingsProviderInfo()
        {
            EnabledTuners = new string[] { };
            EnableAllTuners = true;
        }
    }
}
