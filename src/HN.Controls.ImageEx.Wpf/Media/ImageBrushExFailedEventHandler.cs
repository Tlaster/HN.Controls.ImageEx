﻿using System.Runtime.InteropServices;

namespace HN.Media
{
    /// <summary>
    /// 表示将处理无法加载图像源的事件的方法。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件数据。</param>
    public delegate void ImageBrushExFailedEventHandler([In] object sender, [In] ImageBrushExFailedEventArgs e);
}
