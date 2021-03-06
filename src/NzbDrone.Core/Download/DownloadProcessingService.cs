using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download
{
    public class DownloadProcessingService : IExecute<ProcessMonitoredDownloadsCommand>
    {
        private readonly IConfigService _configService;
        private readonly ICompletedDownloadService _completedDownloadService;
        private readonly IFailedDownloadService _failedDownloadService;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public DownloadProcessingService(IConfigService configService,
                                         ICompletedDownloadService completedDownloadService,
                                         IFailedDownloadService failedDownloadService,
                                         ITrackedDownloadService trackedDownloadService,
                                         IEventAggregator eventAggregator,
                                         Logger logger)
        {
            _configService = configService;
            _completedDownloadService = completedDownloadService;
            _failedDownloadService = failedDownloadService;
            _trackedDownloadService = trackedDownloadService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        private void RemoveCompletedDownloads(List<TrackedDownload> trackedDownloads)
        {
            foreach (var trackedDownload in trackedDownloads.Where(c => c.DownloadItem.CanBeRemoved && c.State == TrackedDownloadState.Imported))
            {
                _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload));
            }
        }

        public void Execute(ProcessMonitoredDownloadsCommand message)
        {
            var enableCompletedDownloadHandling = _configService.EnableCompletedDownloadHandling;
            var removeCompletedDownloads = _configService.RemoveCompletedDownloads;
            var trackedDownloads = _trackedDownloadService.GetTrackedDownloads()
                                              .Where(t => t.IsTrackable)
                                              .ToList();

            foreach (var trackedDownload in trackedDownloads)
            {
                try
                {
                    if (trackedDownload.State == TrackedDownloadState.FailedPending)
                    {
                        _failedDownloadService.ProcessFailed(trackedDownload);
                        continue;
                    }

                    if (enableCompletedDownloadHandling && trackedDownload.State == TrackedDownloadState.ImportPending)
                    {
                        _completedDownloadService.Import(trackedDownload);
                        continue;
                    }

                    if (removeCompletedDownloads &&
                        trackedDownload.DownloadItem.Removed &&
                        trackedDownload.DownloadItem.CanBeRemoved &&
                        trackedDownload.State == TrackedDownloadState.Imported)
                    {
                        _eventAggregator.PublishEvent(new DownloadCanBeRemovedEvent(trackedDownload));
                    }
                }
                catch (Exception e)
                {
                    _logger.Debug(e, "Failed to process download: {0}", trackedDownload.DownloadItem.Title);
                }
            }

            _eventAggregator.PublishEvent(new DownloadsProcessedEvent());
        }
    }
}
