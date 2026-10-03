using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MediaX
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly string AppFolder;
        private readonly string VideosFolder;
        private readonly string PupilsFolder;
        private readonly string ShortsFolder;
        private readonly string DislikesFile;
        private readonly DispatcherTimer _posTimer;
        private List<VideoItem> _allVideos = new();
        private Dictionary<string, int> _dislikes = new();

        public MainWindow()
        {
            InitializeComponent();

            AppFolder = AppContext.BaseDirectory;
            VideosFolder = Path.Combine(AppFolder, "Videos");
            PupilsFolder = Path.Combine(AppFolder, "Pupils");
            ShortsFolder = Path.Combine(AppFolder, "Shorts");
            DislikesFile = Path.Combine(AppFolder, "dislikes.json");

            Directory.CreateDirectory(VideosFolder);
            Directory.CreateDirectory(PupilsFolder);
            Directory.CreateDirectory(ShortsFolder);

            LoadDislikes();
            LoadPupils();
            LoadFeed();

            _posTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _posTimer.Tick += PosTimer_Tick;
            _posTimer.Start();

            Player.MediaEnded += (s, e) => StopPlayback();
        }

        #region Data models
        private record VideoItem(string Title, string Path, string Owner, int Dislikes);
        #endregion

        private void LoadDislikes()
        {
            try
            {
                if (File.Exists(DislikesFile))
                {
                    var json = File.ReadAllText(DislikesFile);
                    _dislikes = JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new();
                }
            }
            catch
            {
                _dislikes = new();
            }
        }

        private void SaveDislikes()
        {
            try
            {
                var json = JsonSerializer.Serialize(_dislikes, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(DislikesFile, json);
            }
            catch
            {
                // ignore persistence errors for now
            }
        }

        private void LoadFeed(string ownerFilter = null)
        {
            _allVideos.Clear();

            // Global videos
            if (Directory.Exists(VideosFolder))
            {
                foreach (var f in Directory.EnumerateFiles(VideosFolder).Where(IsVideoFile))
                {
                    var title = System.IO.Path.GetFileName(f);
                    var dislikes = _dislikes.GetValueOrDefault(f);
                    _allVideos.Add(new VideoItem(title, f, "Global", dislikes));
                }
            }

            // Pupils videos
            if (Directory.Exists(PupilsFolder))
            {
                foreach (var pupilDir in Directory.EnumerateDirectories(PupilsFolder))
                {
                    var owner = System.IO.Path.GetFileName(pupilDir);
                    foreach (var f in Directory.EnumerateFiles(pupilDir).Where(IsVideoFile))
                    {
                        var title = System.IO.Path.GetFileName(f);
                        var dislikes = _dislikes.GetValueOrDefault(f);
                        _allVideos.Add(new VideoItem(title, f, owner, dislikes));
                    }
                }
            }

            var list = string.IsNullOrWhiteSpace(ownerFilter)
                ? _allVideos.OrderByDescending(v => v.Title).ToList()
                : _allVideos.Where(v => v.Owner.Equals(ownerFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            FeedList.ItemsSource = list;
        }

        private static bool IsVideoFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".mp4" or ".mov" or ".wmv" or ".mkv" => true,
                _ => false,
            };
        }

        private void LoadPupils()
        {
            var pupils = new List<string>();
            if (Directory.Exists(PupilsFolder))
            {
                pupils.AddRange(Directory.EnumerateDirectories(PupilsFolder).Select(Path.GetFileName));
            }

            PupilsList.ItemsSource = pupils;
        }

        private VideoItem GetSelectedVideo() => FeedList.SelectedItem as VideoItem;

        private void FeedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = GetSelectedVideo();
            if (item == null) return;

            NowPlayingText.Text = $"{item.Title} — {item.Owner}";
            try
            {
                Player.Source = new Uri(item.Path);
                Player.Play();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Cannot play file: {ex.Message}", "Playback error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PlayBtn_Click(object sender, RoutedEventArgs e)
        {
            Player.Play();
        }

        private void PauseBtn_Click(object sender, RoutedEventArgs e)
        {
            Player.Pause();
        }

        private void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            StopPlayback();
        }

        private void StopPlayback()
        {
            Player.Stop();
            PositionBar.Value = 0;
        }

        private void LikeBtn_Click(object sender, RoutedEventArgs e)
        {
            // "Watch" simulates opening/playing which is already covered by Play.
            var item = GetSelectedVideo();
            if (item == null) return;
            Player.Play();
        }

        private void DislikeBtn_Click(object sender, RoutedEventArgs e)
        {
            var item = GetSelectedVideo();
            if (item == null) return;

            _dislikes.TryGetValue(item.Path, out var count);
            count++;
            _dislikes[item.Path] = count;
            SaveDislikes();

            // Refresh UI
            LoadFeed(GetCurrentPupilFilter());
        }

        private string GetCurrentPupilFilter()
        {
            return PupilsList.SelectedItem as string;
        }

        private void PosTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (Player.NaturalDuration.HasTimeSpan && Player.NaturalDuration.TimeSpan.TotalSeconds > 0)
                {
                    PositionBar.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
                    PositionBar.Value = Player.Position.TotalSeconds;
                }
                else
                {
                    PositionBar.Value = 0;
                    PositionBar.Maximum = 1;
                }
            }
            catch
            {
                // ignore
            }
        }

        private async void MakeShortBtn_Click(object sender, RoutedEventArgs e)
        {
            var item = GetSelectedVideo();
            if (item == null)
            {
                MessageBox.Show("Select a video from the feed first.", "No video selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var input = item.Path;
            var outputFileName = $"{Path.GetFileNameWithoutExtension(input)}_short_{DateTime.Now:yyyyMMddHHmmss}.mp4";
            var output = Path.Combine(ShortsFolder, outputFileName);

            // Basic 15 second short starting at 0. Requires ffmpeg on PATH.
            var args = $"-y -ss 0 -t 15 -i \"{input}\" -c copy \"{output}\"";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                var proc = Process.Start(psi);
                if (proc == null)
                {
                    MessageBox.Show("Failed to start ffmpeg. Ensure ffmpeg is installed and on PATH.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Read output (avoid deadlocks)
                string stderr = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();

                if (proc.ExitCode == 0 && File.Exists(output))
                {
                    MessageBox.Show($"Short created: {output}", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"ffmpeg failed. See message: {stderr}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating short: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddPupil_Click(object sender, RoutedEventArgs e)
        {
            var name = (NewPupilBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Enter a pupil name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dir = Path.Combine(PupilsFolder, name);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            LoadPupils();
            NewPupilBox.Text = string.Empty;
        }

        private void PupilsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = PupilsList.SelectedItem as string;
            LoadFeed(selected);
        }
    }

    // Helper extension for Dictionary
    internal static class DictExtensions
    {
        public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> d, TKey key)
        {
            if (d.TryGetValue(key, out var v)) return v;
            return default!;
        }
    }
}