﻿using System.Diagnostics;
using System.Reflection;
using System.Text;
using Octokit;
using Saku_Overclock.Contracts.Services;
using Saku_Overclock.Helpers;
using Saku_Overclock.ViewModels;
using Application = Microsoft.UI.Xaml.Application;
using FileMode = System.IO.FileMode;
using Package = Windows.ApplicationModel.Package;

namespace Saku_Overclock.SMUEngine;
public abstract class UpdateChecker
{
    private static readonly Version CurrentVersion = RuntimeHelper.IsMSIX ?
        new Version(Package.Current.Id.Version.Major,
            Package.Current.Id.Version.Minor,
            Package.Current.Id.Version.Build,
            Package.Current.Id.Version.Revision)
        : Assembly.GetExecutingAssembly().GetName().Version!;

    private const string RepoOwner = "Erruar";
    private const string RepoName = "Saku-Overclock";
    private static double _downloadPercent; // Процент скачивания
    private static string _timeElapsed = "0:00"; // Время, прошедшее с начала скачивания
    private static string _timeLeft = "0:01"; // Время, оставшееся до завершения скачивания

    public static string? GitHubInfoString
    {
        get; private set;
    }

    public static string CurrentSubVersion
    {
        get;
    } = ГлавнаяViewModel.GetVersion();

    private static Release? _updateNewVersion;

    public static async Task CheckForUpdates()
    {
        var client = new GitHubClient(new ProductHeaderValue("Saku-Overclock-Updater"));
        var releases = await client.Repository.Release.GetAll(RepoOwner, RepoName);

        // Выбираем релиз с самой высокой версией
        var latestRelease = releases
            .Select(r => new { Release = r, Version = ParseVersion(r.TagName) })
            .OrderByDescending(r => r.Version)
            .FirstOrDefault();

        if (latestRelease == null)
        {
            await App.MainWindow.ShowMessageDialogAsync("Не удалось найти релизы на GitHub.", "Ошибка");
            return;
        }
        _updateNewVersion = latestRelease.Release;
        if (latestRelease.Version > CurrentVersion)
        {
            var navigationService = App.GetService<INavigationService>();
            navigationService.NavigateTo(typeof(ОбновлениеViewModel).FullName!, null, true);
        }
    }
    public static Release? GetNewVersion()
    {
        return _updateNewVersion;
    }

    public static async Task GenerateReleaseInfoString()
    {
        try
        {

            var client = new GitHubClient(new ProductHeaderValue("Saku-Overclock-Updater"));
            var releases = await client.Repository.Release.GetAll(RepoOwner, RepoName);

            var sb = new StringBuilder();
            foreach (var release in releases.OrderByDescending(r => r.CreatedAt))
            {
                sb.AppendLine($"{release.Body}".Replace("### THATS ALL?\r\nDon't think that I'm not developing a project, I'm doing it every day for you friends, but so far I can't make a stable update because there are too many changes, but we're getting close to release!\r\nI hope you will appreciate my work as your **star** ⭐ , thank you!", "") + "\n")
                  .AppendLine();
            }

            GitHubInfoString = sb.ToString();
        }
        catch
        {
            GitHubInfoString = "**Failed to fetch info**";
        }
    }

    public static Version ParseVersion(string tagName)
    {
        // Пример тега: "Saku-Overclock-1.0.14.0-Release-Candidate-5"
        var versionString = tagName.Split('-')[2];
        return Version.TryParse(versionString, out var version) ? version : new Version(0, 0, 0, 0);
    }
    public static double GetDownloadPercent()
    {
        return _downloadPercent;
    }
    public static string GetDownloadTimeLeft()
    {
        return _timeLeft;
    }
    public static string GetDownloadTimeElapsed()
    {
        return _timeElapsed;
    }
    public static async Task DownloadAndUpdate(Release release, IProgress<(double percent, string elapsed, string left)> progress)
    {
        var asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe") || a.Name.EndsWith(".msi"));
        if (asset == null)
        {
            await App.MainWindow.ShowMessageDialogAsync("Не удалось найти установочный файл в релизе.", "Ошибка");
            return;
        }

        var downloadUrl = asset.BrowserDownloadUrl;
        var tempFilePath = Path.Combine(Path.GetTempPath(), asset.Name);

        try
        {
            var client = new HttpClient();
            var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L; // Общий размер файла в байтах
            var buffer = new byte[8192];
            var totalRead = 0L;
            var isMoreToRead = true;

            var stopwatch = Stopwatch.StartNew(); // Таймер для отслеживания времени

            var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            var contentStream = await response.Content.ReadAsStreamAsync();
            while (isMoreToRead)
            {
                var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    isMoreToRead = false;
                    continue;
                }

                await fs.WriteAsync(buffer, 0, read);
                totalRead += read;

                // Обновление процентов скачивания
                if (totalBytes > 0)
                {
                    _downloadPercent = (double)totalRead / totalBytes * 100;
                }

                // Обновление времени загрузки
                var elapsed = stopwatch.Elapsed;
                _timeElapsed = $"{elapsed.Minutes}:{elapsed.Seconds:D2}";

                // Оценка оставшегося времени
                if (totalRead > 0 && _downloadPercent > 0)
                {
                    var estimatedTotalTime = TimeSpan.FromMilliseconds(elapsed.TotalMilliseconds / _downloadPercent * 100);
                    var remainingTime = estimatedTotalTime - elapsed;
                    _timeLeft = $"{remainingTime.Minutes}:{remainingTime.Seconds:D2}";
                }

                // Сообщаем о прогрессе в UI, если progress не null
                progress?.Report((_downloadPercent, _timeElapsed, _timeLeft));
            }

            await fs.FlushAsync(); // Убедиться, что все данные записаны на диск

            stopwatch.Stop();
            await fs.DisposeAsync();

            // Убедиться, что файл полностью закрыт перед запуском
            if (File.Exists(tempFilePath))
            {
                label_8:
                try
                {
                    // Запуск загруженного установочного файла с правами администратора
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = tempFilePath,
                        Verb = "runas" // Запуск от имени администратора
                    });

                    // Закрытие текущего приложения
                    Application.Current.Exit();
                    App.MainWindow.Close();
                }
                catch (Exception ex)
                {
                    await App.MainWindow.ShowMessageDialogAsync($"Произошла ошибка при загрузке обновления: {ex.Message}", "Ошибка");
                    await Task.Delay(2000);
                    goto label_8; // Повторить задачу открытия автообновления приложения, в случае если возникла ошибка доступа
                }
                
            }
        }
        catch (Exception ex)
        {
            await App.MainWindow.ShowMessageDialogAsync($"Произошла ошибка при загрузке обновления: {ex.Message}", "Ошибка");
        }
    }

}