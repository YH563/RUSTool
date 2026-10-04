using RUSTool.Communication;
using RUSTool.Replay;
using RUSTool.Services.Logging;
using System;
using System.Collections.ObjectModel;
using System.IO;
using Xunit;

namespace RUSTool.Replay.Tests;

/// <summary>
/// 本地回放引擎（<see cref="LocalReplayPlayer"/>）与目录发现（<see cref="RecordingsLibrary"/>）的单测。
/// 用 RusRecTests 合成的录音文件，不依赖后端 / ROS / 界面。
/// </summary>
public class LocalReplayTests
{
    [Fact]
    public void 目录枚举_只收rusrec且按文件名升序()
    {
        string dir = Path.Combine(Path.GetTempPath(), "rusrec-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "b.rusrec"), "");
            File.WriteAllText(Path.Combine(dir, "a.rusrec"), "");
            File.WriteAllText(Path.Combine(dir, "c.txt"), "");

            var list = RecordingsLibrary.List(dir);

            Assert.Equal(2, list.Count);
            Assert.Equal("a.rusrec", Path.GetFileName(list[0]));
            Assert.Equal("b.rusrec", Path.GetFileName(list[1]));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void 目录枚举_目录不存在时返回空()
        => Assert.Empty(RecordingsLibrary.List(Path.Combine(Path.GetTempPath(), "not-exist-" + Guid.NewGuid().ToString("N"))));

    [Fact]
    public void 引擎_载入并步进_派发状态帧()
    {
        string path = RusRecTests.WriteSampleFile(withIndex: true);
        try
        {
            using var player = new LocalReplayPlayer(new NullLogService());
            BridgeProtocol.StateFrame? state = null;
            player.StateFramePlayed += s => state = s;

            player.Load(path);

            Assert.True(player.IsLoaded);
            Assert.Equal(2, player.RecordCount);
            Assert.True(player.Duration > 0);
            Assert.Equal(ReplayState.Idle, player.State);

            player.Step(); // 第一条是 RobotState（通道 0）

            Assert.NotNull(state);
            Assert.Equal(6, state!.JointPos.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class NullLogService : ILogService
    {
        public ObservableCollection<LogEntry> Entries { get; } = [];

        public void Log(string message, LogLevel level = LogLevel.Info, string source = "")
            => Entries.Add(new LogEntry(DateTime.Now, level, source, message));

        public void Clear() => Entries.Clear();
    }
}
