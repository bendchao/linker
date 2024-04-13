﻿using cmonitor.config;
using common.libs;

namespace cmonitor.plugins.wallpaper.report
{
    public sealed class WallpaperWindows : IWallpaper
    {
        private readonly Config config;
        public WallpaperWindows(Config config)
        {
            this.config = config;
        }
        public void Set(WallpaperConfigInfo info)
        {
            if (info.Open)
            {
                CommandHelper.Windows(string.Empty, new string[] {
                        $"start cmonitor.wallpaper.win.exe \"{info.ImgUrl}\" {config.Data.Client.ShareMemoryKey} {config.Data.Client.ShareMemoryCount} {config.Data.Client.ShareMemorySize} {(int)ShareMemoryIndexs.Keyboard} {(int)ShareMemoryIndexs.Wallpaper}"
                    },false);
            }
        }
    }
}
