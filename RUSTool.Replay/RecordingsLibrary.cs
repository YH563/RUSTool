using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RUSTool.Replay;

/// <summary>
/// 录音目录（`&lt;records_dir&gt;/*.rusrec`）的发现工具 —— 前后端共享文件系统，
/// 后端 recorder 往这里写，前端从这里读。
/// </summary>
public static class RecordingsLibrary
{
    /// <summary>录音扩展名。</summary>
    public const string Extension = ".rusrec";

    /// <summary>把配置的目录解析成绝对路径（相对路径按前端进程工作目录解析；空则按当前目录）。</summary>
    public static string ResolveDirectory(string directory)
        => Path.GetFullPath(string.IsNullOrWhiteSpace(directory) ? "." : directory.Trim());

    /// <summary>
    /// 列出目录下的录音文件（绝对路径），按<b>文件名升序</b> —— 文件名含时间戳，
    /// 因此升序即时间顺序（与后端 replayer 的清单口径一致）。
    /// 目录不存在时返回空列表（不是错误：还没录过而已）。
    /// </summary>
    public static IReadOnlyList<string> List(string directory)
    {
        string full = ResolveDirectory(directory);
        if (!Directory.Exists(full))
            return [];

        return Directory.EnumerateFiles(full, "*" + Extension, SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
    }
}
