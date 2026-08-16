using UnityEditor;
using UnityEngine;

// Testing aid: the record path is only reachable with a score above the stored
// high score, so trying it out means being able to put the bar back on the
// floor. Wipes the record book and the high score together.
public static class RecordsMenu
{
    [MenuItem("Arkanoid/Clear Records")]
    static void ClearRecords()
    {
        RecordBook.Clear();
        PlayerPrefs.DeleteKey(GameManager.HighScoreKey);
        PlayerPrefs.Save();
        Debug.Log("Arkanoid: record book and high score cleared.");
    }
}
