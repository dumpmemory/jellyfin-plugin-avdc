using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AVDC.Extensions;
using Jellyfin.Plugin.AVDC.Helpers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
#if __EMBY__
using MediaBrowser.Model.Logging;

#else
using Microsoft.Extensions.Logging;
#endif

namespace Jellyfin.Plugin.AVDC.ScheduledTasks
{
    public class RefreshGenresTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

#if __EMBY__
        public RefreshGenresTask(ILogManager logManager, ILibraryManager libraryManager)
        {
            _logger = logManager.CreateLogger<RefreshGenresTask>();
            _libraryManager = libraryManager;
        }
#else
        public RefreshGenresTask(ILogger<RefreshGenresTask> logger, ILibraryManager libraryManager)
        {
            _logger = logger;
            _libraryManager = libraryManager;
        }
#endif

        public string Key => $"{Constants.Avdc}RefreshGenres";

        public string Name => "Refresh Genres";

        public string Description => "Refresh metadata genres provided by AVDC in library.";

        public string Category => Constants.Avdc;

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            await Task.Yield();
            progress?.Report(0);

            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                MediaTypes = new[] {MediaType.Video},
                IncludeItemTypes = new[] {nameof(Movie)}
            }).Where(item => item.ProviderIds.ContainsKey(Constants.Avdc)).ToList();

            foreach (var (idx, item) in items.WithIndex())
            {
                progress?.Report((double) idx / items.Count * 100);

                if (item.Genres != null && item.Genres.Any())
                {
                    var genres = item.Genres.ToList();

                    // Add `ChineseSubtitle` Genre
                    if (!genres.Contains(Genres.ChineseSubtitle) &&
                        Genres.HasChineseSubtitle(item.FileNameWithoutExtension))
                        genres.Add(Genres.ChineseSubtitle);

                    // Remove Meaningless Genres
                    genres.RemoveAll(g => Genres.ShouldBeIgnored.Contains(g, StringComparer.OrdinalIgnoreCase));

                    if (!item.Genres.SequenceEqual(genres, StringComparer.Ordinal))
                    {
                        item.Genres = genres.ToArray();
#if __EMBY__
                        _logger.Info("[AVDC] RefreshGenres for video: {0}", item.Name);
                        _libraryManager.UpdateItem(item, item, ItemUpdateType.MetadataEdit);
#else
                        _logger.LogInformation("[AVDC] RefreshGenres for video: {Name}", item.Name);
                        await _libraryManager
                            .UpdateItemAsync(item, item, ItemUpdateType.MetadataEdit, cancellationToken)
                            .ConfigureAwait(false);
#endif
                    }
                }
            }

            progress?.Report(100);
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            yield return new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerWeekly,
                DayOfWeek = DayOfWeek.Sunday,
                TimeOfDayTicks = TimeSpan.FromHours(2).Ticks
            };
        }
    }
}