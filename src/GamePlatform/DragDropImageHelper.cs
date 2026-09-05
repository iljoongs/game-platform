using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;

namespace GamePlatform;

/// <summary>
/// 드래그 앤 드롭된 데이터에서 이미지를 꺼내 임시 파일로 저장한다.
/// 로컬 파일뿐 아니라 웹 브라우저에서 드래그한 이미지(웹 URL, data: URI, 렌더링된 비트맵)도 지원한다.
/// (video-vault 프로젝트에서 이미 검증된 로직을 그대로 이식)
/// </summary>
public static class DragDropImageHelper
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) GamePlatform/1.0");
        return client;
    }

    /// <summary>드롭된 데이터가 이미지로 처리 가능해 보이면 true (DragOver에서 커서 표시용, 실제 네트워크 접근은 하지 않는다).</summary>
    public static bool CanAccept(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop)
        || data.GetDataPresent(DataFormats.Bitmap)
        || data.GetDataPresent("text/uri-list")
        || data.GetDataPresent(DataFormats.Text)
        || data.GetDataPresent(DataFormats.Html);

    /// <summary>
    /// 드롭된 데이터에서 이미지를 파일로 확보해 경로를 반환한다.
    /// 로컬 파일이면 그 경로를 그대로, 웹 URL/data: URI/비트맵이면 임시 파일로 저장한 경로를 반환한다.
    /// 이미지를 찾을 수 없거나 어떤 이유로든 실패하면 null (예외를 던지지 않는다 — OLE 드래그 앤 드롭 콜백 안이라 안전을 위해).
    /// </summary>
    /// <param name="isTemporary">반환된 경로가 이 메서드가 직접 만든 임시 파일(웹 다운로드/data: URI/렌더링된
    /// 비트맵)이면 true, 사용자의 로컬 파일 그대로면 false. 호출자가 사용이 끝난 뒤 파일을 지워도 되는지
    /// 판단하는 데 쓴다 — 로컬 파일(false)은 사용자 소유이므로 절대 지우면 안 된다.</param>
    public static string? TryGetImagePath(IDataObject data, out bool isTemporary)
    {
        isTemporary = false;

        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop) &&
                data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            {
                return files[0];
            }

            foreach (var candidate in ExtractImageCandidates(data))
            {
                var resolved = ResolveCandidate(candidate);
                if (resolved is not null)
                {
                    isTemporary = true;
                    return resolved;
                }
            }

            if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
            {
                isTemporary = true;
                return SaveBitmapToTempFile(bitmap);
            }
        }
        catch
        {
            // 어떤 형식이든 처리 중 실패하면 이미지 없음으로 취급한다.
        }

        return null;
    }

    private static string? ResolveCandidate(string candidate)
    {
        if (candidate.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return TryResolveDataUri(candidate);
        }

        if (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return TryDownloadImage(candidate);
        }

        return null;
    }

    /// <summary>
    /// URL 계열 포맷과 HTML 안의 img 태그(src/data-src/data-iurl/srcset)에서 후보 URL을 모두 모은다.
    /// http(s) URL을 data: URI보다 먼저 시도하도록 정렬한다 (data: 는 대개 저해상도 미리보기이기 때문).
    /// </summary>
    private static IEnumerable<string> ExtractImageCandidates(IDataObject data)
    {
        var candidates = new List<string>();

        foreach (var format in new[] { "text/uri-list", DataFormats.Text, "UniformResourceLocatorW", "UniformResourceLocator" })
        {
            if (TryGetString(data, format) is string text)
            {
                candidates.AddRange(text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line =>
                        line.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("https://", StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (TryGetString(data, DataFormats.Html) is string html)
        {
            foreach (Match m in Regex.Matches(html, "<img[^>]+?(?:src|data-src|data-iurl)\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase))
            {
                candidates.Add(m.Groups[1].Value);
            }

            foreach (Match m in Regex.Matches(html, "srcset\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase))
            {
                var firstUrl = m.Groups[1].Value.Split(',')[0].Trim().Split(' ')[0];
                if (!string.IsNullOrEmpty(firstUrl))
                {
                    candidates.Add(firstUrl);
                }
            }
        }

        // http(s)를 data: 보다 먼저 시도 (안정 정렬: 같은 그룹 내 원래 순서 유지)
        return candidates
            .Distinct()
            .OrderByDescending(c => c.StartsWith("http", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// IDataObject.GetData는 형식에 따라 string 또는 MemoryStream(원시 바이트)을 반환할 수 있어 두 경우를 모두 처리한다.
    /// "...W"로 끝나는 형식(예: UniformResourceLocatorW)은 UTF-16, 그 외(HTML/uri-list 등)는 UTF-8로 해석한다.
    /// </summary>
    private static string? TryGetString(IDataObject data, string format)
    {
        if (!data.GetDataPresent(format))
        {
            return null;
        }

        object? raw;
        try
        {
            raw = data.GetData(format);
        }
        catch
        {
            return null;
        }

        if (raw is string text)
        {
            return text;
        }

        if (raw is MemoryStream stream)
        {
            var bytes = stream.ToArray();
            var encoding = format.EndsWith("W", StringComparison.Ordinal) ? Encoding.Unicode : Encoding.UTF8;
            return encoding.GetString(bytes).TrimEnd('\0');
        }

        return null;
    }

    private static string? TryResolveDataUri(string dataUri)
    {
        var match = Regex.Match(dataUri, "^data:(?<mime>image/[a-zA-Z0-9.+-]+);base64,(?<data>.+)$", RegexOptions.Singleline);
        if (!match.Success)
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(match.Groups["data"].Value);
            var extension = ExtensionFromContentType(match.Groups["mime"].Value) ?? ".jpg";
            var tempPath = Path.Combine(Path.GetTempPath(), $"GamePlatform_{Guid.NewGuid():N}{extension}");
            File.WriteAllBytes(tempPath, bytes);
            return tempPath;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryDownloadImage(string url)
    {
        try
        {
            using var response = HttpClient.GetAsync(url).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            var extension = ExtensionFromContentType(response.Content.Headers.ContentType?.MediaType)
                ?? ExtensionFromUrl(url)
                ?? ".jpg";

            var tempPath = Path.Combine(Path.GetTempPath(), $"GamePlatform_{Guid.NewGuid():N}{extension}");
            File.WriteAllBytes(tempPath, bytes);
            return tempPath;
        }
        catch
        {
            return null;
        }
    }

    private static string SaveBitmapToTempFile(BitmapSource bitmap)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"GamePlatform_{Guid.NewGuid():N}.png");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
        {
            encoder.Save(stream);
        }

        return tempPath;
    }

    private static string? ExtensionFromContentType(string? contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        "image/webp" => ".webp",
        _ => null,
    };

    private static string? ExtensionFromUrl(string url)
    {
        var withoutQuery = url.Split('?', '#')[0];
        var extension = Path.GetExtension(withoutQuery);
        return string.IsNullOrEmpty(extension) || extension.Length > 5 ? null : extension;
    }
}
