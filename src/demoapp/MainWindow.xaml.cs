﻿using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using demoapp.Extensions;
using demoapp.ViewModels;
using JetBrains.Annotations;
using ReactiveUI;
using Snap.Core;

namespace demoapp
{
    [UsedImplicitly]
    public class MainWindow : Window
    {
        readonly ISnapUpdateManagerProgressSource _updateProgressSource;

        public static MainWindowViewModel ViewModel { private get; set; }

        public MainWindow()
        {
            _updateProgressSource = new SnapUpdateManagerProgressSource();

            InitializeComponent();

            App.AttachDevTools(this);
        }

        void InitializeComponent()
        {
            var snapApp = Snapx.Current;
            var currentChannel = snapApp?.Channels.FirstOrDefault(x => x.Current);
            var snapUpdateManager = ViewModel.Environment.UpdateManager;

            ViewModel.CurrentVersion = snapApp?.Version.ToNormalizedString() ?? "-";
            ViewModel.CurrentChannel = currentChannel?.Name ?? "-";
            ViewModel.CurrentFeed = currentChannel?.UpdateFeed.Source?.ToString() ?? "-";
            ViewModel.CurrentApplicationId = snapUpdateManager?.ApplicationId ?? "-";
            ViewModel.NextVersion = "-";
            ViewModel.CommandCheckForUpdates = ReactiveCommand.CreateFromTask(CommandCheckForUpdatesAsync);
            ViewModel.CommandRestartApplication = ReactiveCommand.Create( () =>
            {
                Snapx.StartSupervisor();
                Environment.Exit(0);
            });
            ViewModel.IsSnapPacked = Snapx.Current != null;

            DataContext = ViewModel;

            AvaloniaXamlLoader.Load(this);
        }

        async Task CommandCheckForUpdatesAsync()
        {
            var snapApp = ViewModel.Environment.SnapApp;
            var updateManager = ViewModel.Environment.UpdateManager;

            if (snapApp == null)
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    TransitionView(x => x.ViewIsCheckingForUpdates);
                    await Task.Delay(TimeSpan.FromSeconds(3));
                    TransitionView(x => x.ViewIsDefault);
                    ViewModel.CurrentVersion = "You need to publish this application in order to check for updates";
                });
                return;
            }

            snapApp = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var isViewTransitioned = false;

                void UpdateProgress(string type, int totalPercentage,
                    long releasesChecksummed = 0, long releasesToChecksum = 0,
                    long releasesDownloaded = 0, long releasesToDownload = 0,
                    long filesToRestore = 0, long filesRestored = 0,
                    long totalBytesDownloaded = 0, long totalBytesToDownload = 0)
                {
                    void SetProgressText(long current, long total, string defaultText, string pluralText)
                    {
                        if (!isViewTransitioned && totalPercentage <= 0)
                        {
                            isViewTransitioned = true;
                            TransitionView(x => x.ViewIsApplyingUpdates);
                        }
                        
                        var outputText = total > 1 ? pluralText : defaultText;

                        switch (type)
                        {
                            case "Download":
                                if (total > 1)
                                {
                                    ViewModel.UpdateProgressText =
                                        $"{outputText} ({totalPercentage}%): {current} of {total}. " +
                                        $"Downloaded so far: {totalBytesDownloaded.BytesAsHumanReadable()}. " +
                                        $"Total: {totalBytesToDownload.BytesAsHumanReadable()}";

                                    goto incrementProgress;
                                }

                                ViewModel.UpdateProgressText = $"{outputText} ({totalPercentage}%): " +
                                                               $"Downloaded so far: {totalBytesDownloaded.BytesAsHumanReadable()}. " +
                                                               $"Total: {totalBytesToDownload.BytesAsHumanReadable()}";

                                goto incrementProgress;
                            default:
                                if (total > 1)
                                {
                                    ViewModel.UpdateProgressText = $"{outputText} ({totalPercentage}%): {current} of {total}.";
                                    goto incrementProgress;
                                }

                                ViewModel.UpdateProgressText = $"{outputText}: {totalPercentage}%";
                                goto incrementProgress;
                        }

                        incrementProgress:
                        ViewModel.UpdateProgressPercentage = totalPercentage;
                    }

                    switch (type)
                    {
                        case "Checksum":
                            SetProgressText(releasesChecksummed, releasesToChecksum, "Validating payload", "Validating payloads");
                            break;
                        case "Download":
                            SetProgressText(releasesDownloaded, releasesToDownload, "Downloading payload", "Downloading payloads");
                            break;
                        case "Restore":
                            SetProgressText(filesToRestore, filesRestored, "Restoring file", "Restoring files");
                            break;
                    }
                }

                _updateProgressSource.ChecksumProgress = x => UpdateProgress("Checksum",
                    x.progressPercentage,
                    x.releasesChecksummed,
                    x.releasesToChecksum);
                    
                _updateProgressSource.DownloadProgress = x => UpdateProgress("Download",
                    x.progressPercentage,
                    releasesDownloaded: x.releasesDownloaded,
                    releasesToDownload: x.releasesToDownload,
                    totalBytesDownloaded: x.totalBytesDownloaded,
                    totalBytesToDownload: x.totalBytesToDownload);
                    
                _updateProgressSource.RestoreProgress = x => UpdateProgress("Restore",
                    x.progressPercentage,
                    filesRestored: x.filesRestored,
                    filesToRestore: x.filesToRestore);

                TransitionView(x => x.ViewIsCheckingForUpdates);

                return await updateManager.UpdateToLatestReleaseAsync(_updateProgressSource);
            });

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (snapApp == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1.5));

                    TransitionView(x => x.ViewIsDefault);
                    ViewModel.NextVersion = "Your application is UpToDate\u2122 :)";
                    return;
                }

                ViewModel.NextVersion = $"Version: {snapApp.Version.ToNormalizedString()}";
                ViewModel.ReleaseNotes = $"Release notes: {snapApp.ReleaseNotes}";
                TransitionView(x => x.ViewIsUpdateCompleted);
            });
        }

        static void TransitionView([NotNull] Expression<Func<MainWindowViewModel, object>> expression)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));

            var propertyName = expression.BuildMemberName();
            var propertyInfos = typeof(MainWindowViewModel).GetProperties().ToList();
            foreach (var property in propertyInfos.Where(x => x.Name.StartsWith("ViewIs")))
            {
                if (string.Equals(propertyName, property.Name))
                {
                    property.SetValue(ViewModel, true);
                    continue;
                }

                property.SetValue(ViewModel, false);
            }
        }
    }
}
