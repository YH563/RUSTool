namespace RUSTool.Settings;

/// <summary>设置的持久化出口。读取一律返回可用对象（读不到就默认值），保存失败只上报不抛。</summary>
public interface ISettingsStore
{
    /// <summary>读取设置；文件不存在 / 损坏时返回默认值（不抛）。</summary>
    AppSettings Load();

    /// <summary>保存设置；失败返回 false（不抛）——设置落盘失败不该让应用崩掉。</summary>
    bool Save(AppSettings settings);

    /// <summary>底层存储位置（诊断 / 在界面上展示用）。</summary>
    string Location { get; }
}
