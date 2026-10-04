using System.Text.Json;
using RUSTool.Communication;
using Xunit;

namespace RUSTool.Core.Tests;

/// <summary>
/// <see cref="BridgeProtocol"/> 的编解码单测，重点锁录制 / 回放新引入的两个通道：
///
///   ① **请求的 <c>text</c> 字段**（协议 v0.5）：字符串参数（如 <c>replay_load_path</c> 的路径）
///      走这里；普通浮点指令必须带空串（后端把非空 text 视为该指令的非法参数）。
///   ② **回执的 <c>strings</c> 字段**（协议 v0.4）：录制 / 回放的文件名清单、当前文件名都从这里取；
///      老后端不带该字段时前端要能退化成空数组而不是解析失败。
///
/// 纯函数，不需要网络 / 后端。
/// </summary>
public class BridgeProtocolTests
{
    [Fact]
    public void 编码_普通指令_text默认为空串()
    {
        var json = BridgeProtocol.Encode(new BridgeProtocol.Command(7, "movej", [0.1, 0.2]));

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(7u, doc.RootElement.GetProperty("id").GetUInt32());
        Assert.Equal("movej", doc.RootElement.GetProperty("cmd").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("args").GetArrayLength());
        Assert.Equal("", doc.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public void 编码_replay_load_path_携带路径()
    {
        var json = BridgeProtocol.Encode(
            new BridgeProtocol.Command(4, "replay_load_path", [], "/abs/run_20260930_153850.rusrec"));

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("/abs/run_20260930_153850.rusrec", doc.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public void 解析_reply_带strings()
    {
        const string json = """
            {"type":"reply","id":3,"success":true,"message":"4 个录音文件",
             "result":[4.0,0.0],"strings":["a.rusrec","b.rusrec"]}
            """;

        var msg = BridgeProtocol.TryParseReply(json);

        Assert.NotNull(msg);
        Assert.True(msg!.Success);
        Assert.Equal(2, msg.Result.Length);
        Assert.NotNull(msg.Strings);
        Assert.Equal(2, msg.Strings!.Length);
        Assert.Equal("a.rusrec", msg.Strings[0]);
    }

    [Fact]
    public void 解析_reply_缺strings时退化为空数组()
    {
        const string json = """{"type":"reply","id":1,"success":true,"message":"ok","result":[]}""";

        var msg = BridgeProtocol.TryParseReply(json);

        Assert.NotNull(msg);
        Assert.NotNull(msg!.Strings);
        Assert.Empty(msg.Strings!);
    }

    [Fact]
    public void 解析_event_replay_done()
    {
        const string json = """
            {"type":"event","id":0,"ack_id":7,"event":"replay_done",
             "success":true,"message":"done","result":[30],"strings":[]}
            """;

        var msg = BridgeProtocol.TryParseReply(json);

        Assert.NotNull(msg);
        Assert.Equal("event", msg!.Type);
        Assert.Equal("replay_done", msg.Event);
        Assert.Equal(7u, msg.AckId);
    }

    [Fact]
    public void 解析_坏json返回null()
    {
        Assert.Null(BridgeProtocol.TryParseReply("{ not json"));
    }

    [Fact]
    public void 解析_recorder_status_七项按序读出()
    {
        const string json = """
            {"type":"reply","id":5,"success":true,"message":"","result":[1,15234,96.7,12.4,0,3,1],
             "strings":["run_20260926_141530.rusrec"]}
            """;

        var msg = BridgeProtocol.TryParseReply(json);

        Assert.NotNull(msg);
        Assert.Equal(7, msg!.Result.Length);
        Assert.Equal(1, (int)msg.Result[0]);          // recording
        Assert.Equal(15234, (int)msg.Result[1]);      // records
        Assert.Equal("run_20260926_141530.rusrec", msg.Strings![0]);
    }
}
