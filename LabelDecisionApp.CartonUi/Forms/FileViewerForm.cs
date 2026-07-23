namespace LabelDecisionApp.CartonUi.Forms;

/// <summary>
/// Simple text-file viewer, used for F7 (Event Log) and F8 (Statistics Report).
/// Equivalent to the Progress <c>FileViewer.p</c> call.
/// </summary>
public class FileViewerForm : Form
{
    public FileViewerForm(string title, string filePath)
    {
        Text = title;
        ClientSize = new Size(920, 640);
        BackColor = Color.Black;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode is Keys.Escape or Keys.F4) Close(); };

        var rtb = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Courier New", 9.5f),
            BackColor = Color.Black,
            ForeColor = Color.LightGreen,
            ScrollBars = RichTextBoxScrollBars.Both,
            WordWrap = false,
        };

        var closeBtn = new Button
        {
            Text = "Close  [F4 / Esc]",
            Dock = DockStyle.Bottom,
            Height = 38,
            BackColor = Color.DarkSlateGray,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f),
        };
        closeBtn.Click += (_, _) => Close();

        Controls.Add(rtb);
        Controls.Add(closeBtn);

        try
        {
            rtb.Text = File.ReadAllText(filePath);
            // Scroll to end (matches Progress "END" parameter in FileViewer.p)
            rtb.SelectionStart = rtb.Text.Length;
            rtb.ScrollToCaret();
        }
        catch (Exception ex)
        {
            rtb.Text = $"Error reading file:\n{ex.Message}";
        }
    }
}
