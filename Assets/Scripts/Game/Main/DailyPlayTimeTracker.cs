using System;
using System.Globalization;
using UnityEngine;

public class DailyPlayTimeTracker
{
    public const int DailyLimitMinutes = 45;

    const string DateKey = "Chris.DailyPlayTime.Date";
    const string SecondsKey = "Chris.DailyPlayTime.Seconds";
    const string WarningDateKey = "Chris.DailyPlayTime.WarningDate";

    const float TenMinuteWarningTotalSeconds = (DailyLimitMinutes - 10) * 60f;

    DateTime m_Date = DateTime.Now.Date;
    float m_TotalSeconds;
    float m_UnsavedSeconds;
    bool m_WarningShown;
    bool m_WarningPending;

    public DailyPlayTimeTracker()
    {
        LoadDay(DateTime.Now.Date);
    }

    public bool IsLimitReached
    {
        get
        {
            RefreshDay();
            return m_TotalSeconds >= DailyLimitMinutes * 60f;
        }
    }

    public string GetStatusText()
    {
        RefreshDay();
        var minutes = Mathf.FloorToInt(m_TotalSeconds / 60f);
        var seconds = Mathf.FloorToInt(m_TotalSeconds % 60f);
        return "Today play time: " + minutes + "m " + seconds + "s / " + DailyLimitMinutes + "m";
    }

    public string GetLimitMessage()
    {
        return "今天的累计游戏时长已经超过 " + DailyLimitMinutes + " 分钟，请休息。";
    }

    public string GetTenMinuteWarningMessage()
    {
        return "今天的游戏时长还有10分钟";
    }

    public bool ConsumeTenMinuteWarning()
    {
        RefreshDay();
        if (!m_WarningPending || IsLimitReached)
            return false;

        m_WarningShown = true;
        m_WarningPending = false;
        Flush();
        GameDebug.Log("Daily play time ten-minute warning shown");
        return true;
    }

    public bool Record(float deltaTime, bool isPlayable)
    {
        RefreshDay();
        if (IsLimitReached)
            return true;

        if (deltaTime <= 0f || !isPlayable)
            return false;

        m_TotalSeconds += Mathf.Min(deltaTime, 1f);
        m_UnsavedSeconds += Mathf.Min(deltaTime, 1f);

        if (!m_WarningShown && m_TotalSeconds >= TenMinuteWarningTotalSeconds)
        {
            m_WarningShown = true;
            m_WarningPending = true;
            Flush();
        }

        if (m_TotalSeconds >= DailyLimitMinutes * 60f)
        {
            m_TotalSeconds = DailyLimitMinutes * 60f;
            Flush();
            return true;
        }

        if (m_UnsavedSeconds >= 1f)
            Flush();

        return false;
    }

    public void Flush()
    {
        PlayerPrefs.SetString(DateKey, FormatDay(m_Date));
        PlayerPrefs.SetFloat(SecondsKey, m_TotalSeconds);
        if (m_WarningShown)
            PlayerPrefs.SetString(WarningDateKey, FormatDay(m_Date));
        PlayerPrefs.Save();
        m_UnsavedSeconds = 0f;
    }

    void RefreshDay()
    {
        var today = DateTime.Now.Date;
        if (today != m_Date)
            LoadDay(today);
    }

    void LoadDay(DateTime date)
    {
        m_Date = date;
        var savedDate = PlayerPrefs.GetString(DateKey, string.Empty);
        m_TotalSeconds = savedDate == FormatDay(date)
            ? Mathf.Max(0f, PlayerPrefs.GetFloat(SecondsKey, 0f))
            : 0f;
        m_WarningShown = m_TotalSeconds > 0f &&
            PlayerPrefs.GetString(WarningDateKey, string.Empty) == FormatDay(date);
        m_WarningPending = m_TotalSeconds >= TenMinuteWarningTotalSeconds && !m_WarningShown;
        m_UnsavedSeconds = 0f;
    }

    static string FormatDay(DateTime date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
