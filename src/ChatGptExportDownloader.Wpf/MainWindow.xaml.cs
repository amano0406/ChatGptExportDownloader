using System.IO;
using System.Windows;
using ChatGptExportDownloader.Core;
using WinForms = System.Windows.Forms;

namespace ChatGptExportDownloader.Wpf;

public partial class MainWindow : Window
{
    private CapturedRequest? _request;
    private ProbeResult? _probeResult;
    private CancellationTokenSource? _operationCts;

    public MainWindow()
    {
        InitializeComponent();
        OutputDirectoryTextBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
    }

    private void PasteFromClipboard_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Clipboard.ContainsText())
        {
            CurlTextBox.Text = System.Windows.Clipboard.GetText();
            AppendLog("clipboard pasted");
            SetStatus("貼り付け済み");
        }
    }

    private void Analyze_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _request = CurlCommandParser.Parse(CurlTextBox.Text);
            OutputFileNameTextBox.Text = _request.FileName;
            UrlStatusText.Text = "OK";
            CookieStatusText.Text = _request.HasCookie ? "検出済み" : "未検出";
            UserAgentStatusText.Text = _request.HasUserAgent ? "検出済み" : "未検出";
            FileNameText.Text = _request.FileName;
            RedactedUrlText.Text = _request.RedactedUrl;
            SetStatus("解析OK");
            AppendLog($"analyze OK file={_request.FileName}");
        }
        catch (Exception ex)
        {
            _request = null;
            SetStatus("解析失敗");
            AppendLog($"analyze failed: {ex.Message}");
            System.Windows.MessageBox.Show(this, ex.Message, "解析失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Probe_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureRequest())
        {
            return;
        }

        StartOperation();
        try
        {
            SetStatus("Probe中");
            AppendLog("range probe started");
            var client = new RangeProbeClient();
            _probeResult = await client.ProbeAsync(_request!, _operationCts!.Token);
            HttpStatusText.Text = _probeResult.StatusCode.ToString();
            RangeStatusText.Text = _probeResult.RangeSupported ? "対応" : "未確認";
            TotalSizeText.Text = _probeResult.DisplaySize;
            MetadataText.Text = $"{_probeResult.ETag ?? "-"} / {_probeResult.LastModified?.ToString("u") ?? "-"}";
            SetStatus(_probeResult.RangeSupported ? "Range対応" : "Probe完了");
            AppendLog($"range probe OK status={_probeResult.StatusCode} size={_probeResult.DisplaySize}");
        }
        catch (OperationCanceledException)
        {
            SetStatus("停止");
            AppendLog("range probe canceled");
        }
        catch (Exception ex)
        {
            SetStatus("Probe失敗");
            AppendLog($"range probe failed: {ex.Message}");
            System.Windows.MessageBox.Show(this, ex.Message, "Probe失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            EndOperation();
        }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureRequest())
        {
            return;
        }

        if (!TryBuildDownloadOptions(out var options))
        {
            return;
        }

        StartOperation();
        try
        {
            SetStatus("ダウンロード中");
            AppendLog($"download started file={options.SafeOutputFileName} chunkMiB={options.ChunkMiB}");
            var progress = new Progress<DownloadProgress>(UpdateProgress);
            var result = await new RangeDownloadService().DownloadAsync(_request!, options, progress, _operationCts!.Token);
            SetStatus("完了");
            AppendLog($"download completed path={result.FinalPath}");
            if (result.ZipVerification is not null)
            {
                var zipStatus = result.ZipVerification.IsValid
                    ? $"ZIP OK entries={result.ZipVerification.EntryCount}"
                    : $"ZIP NG {result.ZipVerification.ErrorMessage}";
                AppendLog(zipStatus);
                MetadataText.Text = zipStatus;
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("停止");
            AppendLog("download canceled");
        }
        catch (Exception ex)
        {
            SetStatus("ダウンロード失敗");
            AppendLog($"download failed: {ex.Message}");
            System.Windows.MessageBox.Show(this, ex.Message, "ダウンロード失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            EndOperation();
        }
    }

    private void BrowseOutputDirectory_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "保存先フォルダを選択",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(OutputDirectoryTextBox.Text)
                ? OutputDirectoryTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            OutputDirectoryTextBox.Text = dialog.SelectedPath;
            AppendLog($"output directory selected: {dialog.SelectedPath}");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _operationCts?.Cancel();
    }

    private void ClearSecrets_Click(object sender, RoutedEventArgs e)
    {
        _operationCts?.Cancel();
        CurlTextBox.Clear();
        _request = null;
        _probeResult = null;
        UrlStatusText.Text = "未検出";
        CookieStatusText.Text = "未検出";
        UserAgentStatusText.Text = "未検出";
        FileNameText.Text = "-";
        RedactedUrlText.Text = "-";
        HttpStatusText.Text = "-";
        RangeStatusText.Text = "-";
        TotalSizeText.Text = "-";
        MetadataText.Text = "-";
        DownloadProgressBar.Value = 0;
        ProgressText.Text = "未開始";
        SpeedText.Text = "-";
        CurrentRangeText.Text = "-";
        SetStatus("認証情報消去済み");
        AppendLog("secrets cleared");
    }

    private bool EnsureRequest()
    {
        if (_request is not null)
        {
            return true;
        }

        Analyze_Click(this, new RoutedEventArgs());
        return _request is not null;
    }

    private bool TryBuildDownloadOptions(out RangeDownloadOptions options)
    {
        options = new RangeDownloadOptions("", "");

        var outputDirectory = OutputDirectoryTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            System.Windows.MessageBox.Show(this, "保存先フォルダを指定してください。", "入力不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var outputFileName = OutputFileNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputFileName))
        {
            outputFileName = _request?.FileName ?? "chatgpt-export.zip";
        }

        if (!int.TryParse(ChunkMiBTextBox.Text.Trim(), out var chunkMiB) || chunkMiB <= 0)
        {
            System.Windows.MessageBox.Show(this, "チャンクサイズは正の整数で指定してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        options = new RangeDownloadOptions(outputDirectory, outputFileName, chunkMiB, MaxRetries: 30, VerifyZip: true);
        return true;
    }

    private void UpdateProgress(DownloadProgress progress)
    {
        DownloadProgressBar.Value = Math.Min(100, Math.Max(0, progress.Percent));
        ProgressText.Text = $"{FormatSize(progress.DownloadedBytes)} / {FormatSize(progress.TotalBytes)} ({progress.Percent:0.00}%)";
        SpeedText.Text = $"速度: {FormatSize((long)progress.BytesPerSecond)}/s  リトライ: {progress.RetryCount}";
        CurrentRangeText.Text = $"現在: {progress.CurrentRange}";
    }

    private void StartOperation()
    {
        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
    }

    private void EndOperation()
    {
        _operationCts?.Dispose();
        _operationCts = null;
    }

    private void SetStatus(string status)
    {
        StatusText.Text = status;
    }

    private void AppendLog(string message)
    {
        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }

    private static string FormatSize(long bytes)
    {
        return bytes >= 1_000_000_000
            ? $"{bytes / 1_000_000_000d:0.000} GB"
            : $"{bytes / 1_000_000d:0.0} MB";
    }
}
