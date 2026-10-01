using AppCore;
using Iros.Workshop;
using Newtonsoft.Json.Linq;
using AppUI.Windows;
using AppUI;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace AppUI.Classes
{
    class FFNxDriverUpdater
    {
        private FileVersionInfo _currentDriverVersion = null;
        public static bool IsInitialSetupReady { get; private set; } = true;
        public static bool IsInitialSetupInProgress { get; private set; }
        public static event Action<bool> InitialSetupReadyChanged;

        private static void SetInitialSetupReady(bool ready, bool inProgress = false)
        {
            IsInitialSetupReady = ready;
            IsInitialSetupInProgress = inProgress;
            InitialSetupReadyChanged?.Invoke(ready);
            Sys.Message(new WMessage(ready ? "Initial FFNx setup completed." : "Initial FFNx setup is pending.",
                WMessageLogLevel.LogOnly));
        }

        public static void AbortInitialSetup() => SetInitialSetupReady(false);

        private static bool PreserveCustomDriver(bool notify)
        {
            if (!IsAlreadyInstalled() || !FFNxDeployment.IsCustomManaged(Sys.InstallPath, AppContext.BaseDirectory)) return false;
            Sys.Message(new WMessage("Keeping the custom FFNx build; automatic replacement is disabled for this installation.", WMessageLogLevel.LogOnly));
            if (notify) MessageDialogWindow.Show("This installation uses a custom FFNx build. Updates are managed through the FFNx folder beside 7th Heaven.exe.", "Custom FFNx", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }

        private string GetUpdateInfoPath()
        {
            return Path.Combine(Sys.PathToTempFolder, "ffnxupdateinfo.json");
        }

        public string GetCurrentDriverVersion()
        {
            _currentDriverVersion = null;

            try
            {
                if (IsAlreadyInstalled())
                {
                    _currentDriverVersion = FileVersionInfo.GetVersionInfo(
                        Path.Combine(
                            Sys.InstallPath,
                            Sys.Settings.FF7InstalledVersion == FF7Version.Original98 ? "FFNx.dll" : "AF3DN.P"
                        )
                    );
                }
            }
            catch (FileNotFoundException)
            {
                _currentDriverVersion = null;
            }

            return _currentDriverVersion != null ? _currentDriverVersion.FileVersion : "0.0.0.0";
        }

        private string GetUpdateChannel(FFNxUpdateChannelOptions channel)
        {
            switch(channel)
            {
                case FFNxUpdateChannelOptions.Stable:
                    return "https://api.github.com/repos/julianxhokaxhiu/FFNx/releases/latest";
                case FFNxUpdateChannelOptions.Canary:
                    return "https://api.github.com/repos/julianxhokaxhiu/FFNx/releases/tags/canary";
                default:
                    return "";
            }
        }

        private string GetUpdateVersion(string name)
        {
            return name.Replace("FFNx-v", "");
        }

        private string GetUpdateReleaseUrl(dynamic assets)
        {
            for (int i = 0; i < assets.Count; i++)
            {
                string url = assets[i].browser_download_url.Value;
                string prefix = Sys.Settings.FF7InstalledVersion == FF7Version.Original98 ? "FFNx-FF7_1998" : "FFNx-Steam";

                if (url.Contains(prefix))
                    return url;
            }

            return String.Empty;
        }

        private void SwitchToDownloadPanel()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                MainWindow window = App.Current.MainWindow as MainWindow;

                window.tabCtrlMain.SelectedIndex = 1;
            });
        }

        private void SwitchToModPanel()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                MainWindow window = App.Current.MainWindow as MainWindow;

                window.tabCtrlMain.SelectedIndex = 0;
            });
        }

        public void CheckForUpdates(FFNxUpdateChannelOptions channel, bool manualCheck = false)
        {
            if (PreserveCustomDriver(manualCheck)) return;
            DownloadItem download = new DownloadItem()
            {
                Links = new List<string>() { LocationUtil.FormatHttpUrl(GetUpdateChannel(channel)) },
                SaveFilePath = GetUpdateInfoPath(),
                Category = DownloadCategory.AppUpdate,
                ItemName = $"Checking for FFNx Updates using channel {Sys.Settings.FFNxUpdateChannel.ToString()}..."
            };

            download.IProc = new Install.InstallProcedureCallback(e =>
            {
                bool success = (e.Error == null && e.Cancelled == false);

                if (success)
                {
                    try
                    {
                        StreamReader file = File.OpenText(download.SaveFilePath);
                        dynamic release = JValue.Parse(file.ReadToEnd());
                        file.Close();
                        File.Delete(download.SaveFilePath);

                        Version curVersion = new Version(GetCurrentDriverVersion());
                        Version newVersion = new Version(GetUpdateVersion(release.name.Value));

                        switch (newVersion.CompareTo(curVersion))
                        {
                            case 1: // NEWER
                                if (manualCheck)
                                {
                                    if (
                                        MessageDialogWindow.Show(
                                            $"New FFNx driver version found!\n\nCurrently installed: {curVersion.ToString()}\nNew Version: {newVersion.ToString()}\n\nWould you like to update?",
                                            "Update found!",
                                            System.Windows.MessageBoxButton.YesNo,
                                            System.Windows.MessageBoxImage.Question
                                        ).Result == System.Windows.MessageBoxResult.Yes)
                                        DownloadAndExtract(GetUpdateReleaseUrl(release.assets), newVersion.ToString());
                                }
                                else
                                {
                                    App.Current.Dispatcher.Invoke(() =>
                                    {
                                        MainWindow window = App.Current.MainWindow as MainWindow;

                                        window.ViewModel.FFNxUpdateVersion = $"{newVersion.ToString()} ({Sys.Settings.FFNxUpdateChannel.ToString()})";
                                    });
                                }
                                break;
                            case 0: // SAME
                                if (manualCheck)
                                    MessageDialogWindow.Show("Your FFNx driver version is up to date!", "No update found", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                                break;
                            case -1: // OLDER
                                if (manualCheck)
                                {
                                    if (
                                        MessageDialogWindow.Show(
                                            $"Your FFNx driver version is newer than the one offered by your channel management setting.\n\nCurrently installed: {curVersion.ToString()}\nVersion being offered: {newVersion.ToString()}\n\nContinue with the downgrade?",
                                            "Update found!",
                                            System.Windows.MessageBoxButton.YesNo,
                                            System.Windows.MessageBoxImage.Question
                                        ).Result == System.Windows.MessageBoxResult.Yes)
                                            DownloadAndExtract(GetUpdateReleaseUrl(release.assets), newVersion.ToString());
                                }
                                break;
                        }
                    }
                    catch (Exception)
                    {
                        MessageDialogWindow.Show("Something went wrong while checking for FFNx updates. Please try again later.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        Sys.Message(new WMessage() { Text = $"Could not parse the FFNx release json at {GetUpdateChannel(channel)}", LoggedException = e.Error });
                    }
                }
                else
                {
                    MessageDialogWindow.Show("Something went wrong while checking for FFNx updates. Please try again later.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    Sys.Message(new WMessage() { Text = $"Could not fetch for FFNx updates at {GetUpdateChannel(channel)}", LoggedException = e.Error });
                }
            });

            Sys.Downloads.AddToDownloadQueue(download);
        }

        public void DownloadAndExtractLatestVersion(FFNxUpdateChannelOptions channel, bool initialSetup = false)
        {
            if (initialSetup) SetInitialSetupReady(false, inProgress: true);
            if (PreserveCustomDriver(true))
            {
                if (initialSetup) SetInitialSetupReady(true);
                return;
            }
            if (!initialSetup) SwitchToDownloadPanel();

            DownloadItem download = new DownloadItem()
            {
                Links = new List<string>() { LocationUtil.FormatHttpUrl(GetUpdateChannel(channel)) },
                SaveFilePath = GetUpdateInfoPath(),
                Category = DownloadCategory.AppUpdate,
                ItemName = $"Fetching the latest FFNx version using channel {Sys.Settings.FFNxUpdateChannel.ToString()}..."
            };
            if (initialSetup)
            {
                download.OnError = AbortInitialSetup;
                download.OnCancel = AbortInitialSetup;
            }

            download.IProc = new Install.InstallProcedureCallback(e =>
            {
                bool success = (e.Error == null && e.Cancelled == false);

                if (success)
                {
                    try
                    {
                        StreamReader file = File.OpenText(download.SaveFilePath);
                        dynamic release = JValue.Parse(file.ReadToEnd());
                        file.Close();
                        File.Delete(download.SaveFilePath);

                        Version newVersion = new Version(GetUpdateVersion(release.name.Value));
                        DownloadAndExtract(GetUpdateReleaseUrl(release.assets), newVersion.ToString(), initialSetup);
                    }
                    catch (Exception)
                    {
                        if (initialSetup) SetInitialSetupReady(false);
                        MessageDialogWindow.Show("Something went wrong while checking for FFNx updates. Please try again later.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        Sys.Message(new WMessage() { Text = $"Could not parse the FFNx release json at {GetUpdateChannel(channel)}", LoggedException = e.Error });
                    }
                }
                else
                {
                    if (initialSetup) SetInitialSetupReady(false);
                    MessageDialogWindow.Show("Something went wrong while checking for FFNx updates. Please try again later.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    Sys.Message(new WMessage() { Text = $"Could not fetch for FFNx updates at {GetUpdateChannel(channel)}", LoggedException = e.Error });
                }
            });

            Sys.Downloads.AddToDownloadQueue(download);
        }

        private void DownloadAndExtract(string url, string version, bool initialSetup = false)
        {
            if (PreserveCustomDriver(false))
            {
                if (initialSetup) SetInitialSetupReady(true);
                return;
            }
            if (url != String.Empty)
            {
                if (!initialSetup) SwitchToDownloadPanel();

                DownloadItem download = new DownloadItem()
                {
                    Links = new List<string>() { LocationUtil.FormatHttpUrl(url) },
                    SaveFilePath = Path.Combine(Sys.PathToTempFolder, url.Substring(url.LastIndexOf("/") + 1)),
                    Category = DownloadCategory.AppUpdate,
                    ItemName = $"Downloading FFNx Update {url}..."
                };
                if (initialSetup)
                {
                    download.OnError = AbortInitialSetup;
                    download.OnCancel = AbortInitialSetup;
                }

                download.IProc = new Install.InstallProcedureCallback(e =>
                {
                    bool success = (e.Error == null && e.Cancelled == false);

                    if (success)
                    {
                        try
                        {
                            if (PreserveCustomDriver(false))
                            {
                                if (initialSetup) SetInitialSetupReady(true);
                                return;
                            }
                            if (WineEnvironment.IsRunningInWine())
                            {
                                SafeZipExtractor.ExtractFiles(download.SaveFilePath, Sys.InstallPath);
                            }
                            else
                            {
                                using (var archive = ZipArchive.OpenArchive(download.SaveFilePath, new SharpCompress.Readers.ReaderOptions()
                                {
                                    LeaveStreamOpen = false
                                }))
                                {
                                    foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                                    {
                                        entry.WriteToDirectory(Sys.InstallPath, new ExtractionOptions()
                                        {
                                            ExtractFullPath = true,
                                            Overwrite = true
                                        });
                                    }
                                }
                            }

                            SwitchToModPanel();
                            Sys.FFNxConfig.Backup();
                            Sys.FFNxConfig.Reload();
                            Sys.FFNxConfig.ResetTo7thHeavenDefaults();
                            Sys.FFNxConfig.OverrideInternalKeys();
                            Sys.FFNxConfig.Save();
                            FFNxDeployment.Apply(Sys.InstallPath, AppContext.BaseDirectory);
                            Sys.FFNxConfig.Reload();

                            File.Delete(download.SaveFilePath);

                            MessageDialogWindow.Show($"Successfully updated FFNx to version {version}. All options have been set to default.\n\nEnjoy!", "Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                            Sys.Message(new WMessage() { Text = $"Successfully updated FFNx to version {version}" });
                            if (initialSetup) SetInitialSetupReady(true);
                        }
                        catch (Exception ex)
                        {
                            if (initialSetup) SetInitialSetupReady(false);
                            Sys.Message(new WMessage() { Text = "FFNx installation failed", LoggedException = ex });
                            MessageDialogWindow.Show($"FFNx installation failed: {ex.Message}", "FFNx error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        if (initialSetup) SetInitialSetupReady(false);
                        MessageDialogWindow.Show("Something went wrong while downloading the FFNx update. Please try again later.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        Sys.Message(new WMessage() { Text = $"Could not download the FFNx update {url}", LoggedException = e.Error });
                    }
                });

                Sys.Downloads.AddToDownloadQueue(download);
            }
            else
            {
                if (initialSetup) SetInitialSetupReady(false);
                MessageDialogWindow.Show("Something went wrong while downloading the FFNx update. Please try again later.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        public static void CleanupUnnecessaryFiles()
        {
            // ================================================================================================
            // Always cleanup these files if present, to avoid conflict with various mods
            // ================================================================================================

            Dictionary<string, bool> pathsToDelete = new Dictionary<string, bool>(){
                    { "hext", true },
                };


            string entryPath = "";
            bool entryDeleteRecursive = false;

            foreach (var entry in pathsToDelete)
            {
                entryPath = Sys.InstallPath + "\\" + entry.Key;
                entryDeleteRecursive = entry.Value;

                // Delete recursively
                if (entryDeleteRecursive)
                {
                    if (Directory.Exists(entryPath)) Directory.Delete(entryPath, true);
                }
                else
                {
                    if (File.Exists(entryPath)) File.Delete(entryPath);
                }
            }
        }

        public static bool IsAlreadyInstalled()
        {
            var fi = new FileInfo(
                Path.Combine(Sys.InstallPath, Sys.Settings.FF7InstalledVersion == FF7Version.Original98 ? "FFNx.dll" : "AF3DN.P")
            );
            bool ret = fi.Exists;

            if (Sys.Settings.FF7InstalledVersion == FF7Version.Steam && fi.Exists)
            {
                // Steam driver is not FFNx, force installation
                if (fi.Length <= (192 * 1024)) ret = false;
            }

            return ret;
        }
    }
}
