using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class RecordEntry
{
    public string date;
    public string name;
    public int score;
}

// Chronological list of everyone who ever set a new record, persisted in
// PlayerPrefs as JSON (JsonUtility needs the wrapper class to serialize a list).
public static class RecordBook
{
    const string Key = "Arkanoid.Records";

    [Serializable]
    class RecordList
    {
        public List<RecordEntry> entries = new List<RecordEntry>();
    }

    public static List<RecordEntry> Load()
    {
        var json = PlayerPrefs.GetString(Key, "");
        if (string.IsNullOrEmpty(json)) return new List<RecordEntry>();
        var list = JsonUtility.FromJson<RecordList>(json);
        return list?.entries ?? new List<RecordEntry>();
    }

    public static void Add(string name, int score)
    {
        var list = new RecordList { entries = Load() };
        list.entries.Add(new RecordEntry
        {
            date = DateTime.Now.ToString("yyyy-MM-dd"),
            name = name,
            score = score,
        });
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(list));
        PlayerPrefs.Save();
    }

    // Wipes the book. Nothing in the game reaches this — it is for testing the
    // record path, which needs an empty book to be worth entering at all.
    public static void Clear()
    {
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
    }
}
