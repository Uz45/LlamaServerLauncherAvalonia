using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public static class DialogPositionHelper
{
    public static async Task ApplySavedGeometryAsync(Window window, ConfigurationService configService, string dialogKey)
    {
        var settings = await configService.LoadAppSettingsAsync();

        if (!settings.DialogGeometry.TryGetValue(dialogKey, out var geo))
            return;

        if (geo.Width > 0)
            window.Width = Math.Max(geo.Width, window.MinWidth);
        if (geo.Height > 0)
            window.Height = Math.Max(geo.Height, window.MinHeight);

        if (geo.Left.HasValue && geo.Top.HasValue)
        {
            var left = geo.Left.Value;
            var top = geo.Top.Value;
            if (left >= 0 && left < 5000 && top >= 0 && top < 3000)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = new PixelPoint((int)left, (int)top);
            }
        }
    }

    /// <summary>
    /// Save pre-captured dialog geometry asynchronously. Call from the caller
    /// after ShowDialog returns, NOT from OnClosing (would deadlock on UI thread).
    /// </summary>
    public static async Task SaveCapturedGeometryAsync(DialogGeometry? geometry, ConfigurationService configService, string dialogKey)
    {
        if (geometry == null) return;

        var settings = await configService.LoadAppSettingsAsync();
        settings.DialogGeometry[dialogKey] = geometry;
        await configService.SaveAppSettingsAsync(settings);
    }
}
