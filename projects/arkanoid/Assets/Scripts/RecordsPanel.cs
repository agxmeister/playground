using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// End-of-round overlay: asks for the record holder's name after a new record
// and shows the hall of fame. Display only — GameManager drives it.
public class RecordsPanel : MonoBehaviour
{
    const int VisibleRecords = 8;

    [SerializeField] Text titleText;
    [SerializeField] Text messageText;
    [SerializeField] Text nameText;
    [SerializeField] Text listText;

    public void ShowNameEntry(int score)
    {
        gameObject.SetActive(true);
        titleText.text = "NEW RECORD!";
        messageText.text = $"{score} points! Type your name and press ENTER";
        nameText.text = "_";
        listText.text = "";
    }

    public void SetTypedName(string name) => nameText.text = name + "_";

    public void ShowRecords(IReadOnlyList<RecordEntry> records, string message)
    {
        gameObject.SetActive(true);
        titleText.text = "HALL OF FAME";
        messageText.text = message;
        nameText.text = "";

        if (records.Count == 0)
        {
            listText.text = "No records yet";
            return;
        }

        var lines = new StringBuilder();
        for (int i = records.Count - 1; i >= 0 && i > records.Count - 1 - VisibleRecords; i--)
            lines.AppendLine($"{records[i].date}    {records[i].name}    {records[i].score}");
        listText.text = lines.ToString().TrimEnd();
    }

    public void Hide() => gameObject.SetActive(false);
}
