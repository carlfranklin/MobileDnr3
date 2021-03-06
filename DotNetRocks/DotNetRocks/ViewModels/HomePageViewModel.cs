using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.CompilerServices;
using System.ComponentModel;
using MvvmHelpers;
using System.Windows.Input;
using MvvmHelpers.Commands;
using MediaManager;
using System.Threading.Tasks;
using MonkeyCache.FileStore;
using System.IO;
using System.Net;
using Xamarin.Essentials;
using System.Xml;
using DotNetRocks.Services;
using DotNetRocks.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace DotNetRocks.ViewModels
{
    public class HomePageViewModel : BaseViewModel
    {
        string CacheDir = "";
        string CachedFileName = "";
        FileStream LocalFileStream = null;
        ApiService ApiService = new ApiService();
        public List<int> ShowNumbers { get; set; } = new List<int>();
        public int RecordsToRead { get; set; } = 20;
        public int LastShowNumber { get; set; }

        public HomePageViewModel()
        {
            Barrel.ApplicationId = "mobile_dnr";
            CacheDir = FileSystem.CacheDirectory;
            CrossMediaManager.Current.PositionChanged += Current_PositionChanged;
            CrossMediaManager.Current.MediaItemFinished += Current_MediaItemFinished;

            // Does the file exist?
            if (System.IO.File.Exists(CachedFileName))
            {
                // Yes! We are cached
                IsCached = true;
            }
            var t = Task.Run(() => GetNextBatchOfShows());
            t.Wait();
        }

        public async Task GetNextBatchOfShows()
        {
            if (ShowNumbers.Count == 0)
            {
                ShowNumbers = await ApiService.GetShowNumbers();
                if (ShowNumbers == null || ShowNumbers.Count == 0) return;
                LastShowNumber = ShowNumbers.First<int>() + 1;
            }

            var request = new GetByShowNumbersRequest()
            {
                ShowName = "dotnetrocks",
                Indexes = (from x in ShowNumbers where x < LastShowNumber && x > (LastShowNumber - RecordsToRead) select x).ToList()
            };

            var nextBatch = await ApiService.GetByShowNumbers(request);
            if (nextBatch == null || nextBatch.Count == 0) return;

            AllShows.AddRange(nextBatch);
            LastShowNumber = nextBatch.Last<Show>().ShowNumber;
        }

        private async Task LoadAllShows()
        {
            AllShows = await ApiService.GetAllShows();
            AllShows[0].ShowDetails = await ApiService.GetShowDetails(AllShows[0].ShowNumber);
            CurrentStatus = $"{AllShows.Count} shows downloaded. First show title: {AllShows[0].ShowTitle}. " +
                $"The first guest is {AllShows[0].ShowDetails.Guests[0].Name} " +
                $"and the file can be downloaded at {AllShows[0].ShowDetails.File.Url}";
        }

        private void Current_PositionChanged(object sender, MediaManager.Playback.PositionChangedEventArgs e)
        {
            TimeSpan currentMediaPosition = CrossMediaManager.Current.Position;
            TimeSpan currentMediaDuration = CrossMediaManager.Current.Duration;
            TimeSpan TimeRemaining = currentMediaDuration.Subtract(currentMediaPosition);
            if (IsPlaying)
                CurrentStatus = $"Time Remaining: {TimeRemaining.Minutes:D2}:{TimeRemaining.Seconds:D2}";
        }

        private void Current_MediaItemFinished(object sender, MediaManager.Media.MediaItemEventArgs e)
        {
            CurrentStatus = "";
            IsPlaying = false;
            if (LocalFileStream != null)
            {
                LocalFileStream.Dispose();
            }
        }

        private bool isPlaying;
        public bool IsPlaying
        {
            get
            {
                return isPlaying;
            }
            set
            {
                SetProperty(ref isPlaying, value);
            }
        }

        private ICommand play;
        public ICommand Play
        {
            get
            {
                if (play == null)
                {
                    play = new AsyncCommand<string>(PerformPlay);
                }

                return play;
            }
        }

        private ICommand loadMoreShows;
        public ICommand LoadMoreShows
        {
            get
            {
                if (loadMoreShows == null)
                {
                    loadMoreShows = new AsyncCommand(GetNextBatchOfShows);
                }
                return loadMoreShows;
            }
        }

        public void DownloadFile(string Url)
        {
            var Uri = new Uri(Url);

            WebClient webClient = new WebClient();
            using (webClient)
            {
                webClient.DownloadDataCompleted += (s, e) =>
                {
                    try
                    {
                        System.IO.File.WriteAllBytes(CachedFileName, e.Result);
                        IsCached = true;
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message;
                    }
                };

                webClient.DownloadDataAsync(Uri);
            }
        }

        private async Task PerformPlay(string Url)
        {
            IsPlaying = true;
            string FileNameOnly = Path.GetFileName(Url);
            CachedFileName = Path.Combine(CacheDir, FileNameOnly);

            if (!IsCached)
            {
                // Not in cache. Play from URL
                CurrentStatus = "Downloading...";
                await CrossMediaManager.Current.Play(Url);
                // Download the file to the cache
                DownloadFile(Url);
            }
            else
            {
                // In the cache. Play local file
                CurrentStatus = "Playing from Cache...";
                LocalFileStream = System.IO.File.OpenRead(CachedFileName);
                await CrossMediaManager.Current.Play(LocalFileStream, FileNameOnly);
            }
        }

        private ICommand stop;
        public ICommand Stop
        {
            get
            {
                if (stop == null)
                {
                    stop = new AsyncCommand(PerformStop);
                }
                return stop;
            }
        }

        protected async Task PerformStop()
        {
            IsPlaying = false;
            CurrentStatus = "";
            await CrossMediaManager.Current.Stop();

            if (LocalFileStream != null)
            {
                LocalFileStream.Dispose();
            }
        }

        private string currentStatus;
        public string CurrentStatus
        {
            get => currentStatus;
            set => SetProperty(ref currentStatus, value);
        }

        private bool isCached;
        public bool IsCached
        {
            get => isCached;
            set => SetProperty(ref isCached, value);
        }

        private List<Show> allShows = new List<Show>();
        public List<Show> AllShows
        {
            get => allShows;
            set => SetProperty(ref allShows, value);
        }

    }
}