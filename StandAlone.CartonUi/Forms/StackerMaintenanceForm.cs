using StandAlone.CartonUi.Models;
using StandAlone.CartonUi.Services;
using System.ComponentModel;

namespace StandAlone.CartonUi.Forms;

/// <summary>
/// Maintenance screen for stacker → item assignments (Progress dtmnt060.p, "System Stacker
/// Information"). Stored directly as Stacker#/Item Number/Qty/Shade/Size (the operator-facing
/// format, not the underlying Progress schema — see StackerRecord). Qty is informational,
/// re-resolved from itemdet rather than trusted. Item numbers are restricted to the line's
/// currently-configured Primary/Secondary item (Settings), matching how this plant actually
/// uses it — each stacker is a shade/size variant of one of those two items, never an
/// arbitrary item from the full master. Max 9 stackers per line, matching the legacy
/// "01".."09" auto-numbering cap.
/// </summary>
public class StackerMaintenanceForm : Form
{
    private const int MaxStackers = 9;

    private readonly IBoxRepository _boxRepo;
    private readonly int _lineId;
    private readonly bool _doesMexico;
    private readonly AppSettings _settings;
    private readonly bool _doesTwoPrims;

    private readonly BindingList<StackerRow> _rows = new();
    private DataGridView _grid = null!;
    private Button _btnAdd = null!;
    private Button _btnEdit = null!;
    private Button _btnDelete = null!;
    private Label _statusLabel = null!;

    public StackerMaintenanceForm(IBoxRepository boxRepo, int lineId, bool doesMexico, AppSettings settings, bool doesTwoPrims)
    {
        _boxRepo = boxRepo;
        _lineId = lineId;
        _doesMexico = doesMexico;
        _settings = settings;
        _doesTwoPrims = doesTwoPrims;

        Text = $"Stacker Maintenance — Line {lineId:00}";
        WindowState = FormWindowState.Maximized;
        FormBorderStyle = FormBorderStyle.Sizable;
        BackColor = Color.MidnightBlue;
        ForeColor = Color.White;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        BuildUI();
        Load += async (_, _) => await ReloadAsync();
    }

    private void BuildUI()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.DarkSlateBlue };
        header.Controls.Add(new Label
        {
            Text = $"STACKER MAINTENANCE   Line {_lineId:00}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            ForeColor = Color.LightYellow,
        });
        var btnBack = new Button
        {
            Text = "← Back  (Esc)",
            Size = new Size(140, 38),
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.DarkRed,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f),
        };
        btnBack.Click += (_, _) => Close();
        header.Controls.Add(btnBack);
        Controls.Add(header);

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.FromArgb(30, 30, 70), Padding = new Padding(12, 9, 12, 9) };
        _btnAdd = new Button
        {
            Text = "Add (Enter)", Size = new Size(120, 38), Location = new Point(12, 9),
            FlatStyle = FlatStyle.Flat, BackColor = Color.DarkGreen, ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        };
        _btnAdd.Click += async (_, _) => await OpenEditDialogAsync(null);
        toolbar.Controls.Add(_btnAdd);

        _btnEdit = new Button
        {
            Text = "Edit (Enter)", Size = new Size(120, 38), Location = new Point(140, 9),
            FlatStyle = FlatStyle.Flat, BackColor = Color.DarkSlateBlue, ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f), Enabled = false,
        };
        _btnEdit.Click += async (_, _) => await OpenEditDialogAsync(SelectedRow());
        toolbar.Controls.Add(_btnEdit);

        _btnDelete = new Button
        {
            Text = "Delete (F10)", Size = new Size(120, 38), Location = new Point(268, 9),
            FlatStyle = FlatStyle.Flat, BackColor = Color.DarkRed, ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f), Enabled = false,
        };
        _btnDelete.Click += async (_, _) => await DeleteSelectedAsync();
        toolbar.Controls.Add(_btnDelete);

        _statusLabel = new Label
        {
            Location = new Point(410, 9), Size = new Size(600, 38),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.LightCyan, Font = new Font("Segoe UI", 9.5f),
        };
        toolbar.Controls.Add(_statusLabel);
        Controls.Add(toolbar);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoGenerateColumns = false,
            BackgroundColor = Color.LightSteelBlue,
            BorderStyle = BorderStyle.FixedSingle,
            ForeColor = Color.Black,
            MultiSelect = false,
            Font = new Font("Courier New", 11f),
            RowHeadersVisible = false,
            ColumnHeadersHeight = 30,
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Stacker#",    DataPropertyName = "StackNum",   Width = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Item Number", DataPropertyName = "ItemNumber", Width = 220 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Qty",         DataPropertyName = "Qty",        Width = 90  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Shade",       DataPropertyName = "Shade",      Width = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Size",        DataPropertyName = "Size",       Width = 90  });
        _grid.DataSource = _rows;
        _grid.SelectionChanged += (_, _) => UpdateButtonStates();
        _grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await OpenEditDialogAsync(SelectedRow()); };
        _grid.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) await OpenEditDialogAsync(SelectedRow());
            else if (e.KeyCode == Keys.F10) await DeleteSelectedAsync();
        };
        Controls.Add(_grid);
        _grid.BringToFront();
    }

    private StackerRow? SelectedRow() => _grid.CurrentRow?.DataBoundItem as StackerRow;

    private void UpdateButtonStates()
    {
        var hasSelection = SelectedRow() != null;
        _btnEdit.Enabled = hasSelection;
        _btnDelete.Enabled = hasSelection;
        _btnAdd.Enabled = _rows.Count < MaxStackers;
    }

    private async Task ReloadAsync()
    {
        _statusLabel.Text = "Loading...";
        var stackers = await _boxRepo.GetAllStackersAsync(_lineId, CancellationToken.None);

        _rows.Clear();
        foreach (var s in stackers.OrderBy(s => s.StackNum.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            // Qty is informational (Progress never stores it on the stacker either) — shown
            // fresh from the item master when it still resolves, falling back to whatever was
            // last saved if the item can't be found (e.g. since discontinued).
            ItemDetail? item = !string.IsNullOrWhiteSpace(s.ItemNumber)
                ? await _boxRepo.GetItemDetailByNumberAsync(s.ItemNumber, _doesMexico, CancellationToken.None)
                : null;

            _rows.Add(new StackerRow
            {
                Record     = s,
                StackNum   = s.StackNum.Trim(),
                ItemNumber = string.IsNullOrWhiteSpace(s.ItemNumber) ? "(not set)" : s.ItemNumber,
                Qty        = item?.LisQty ?? s.Qty,
                Shade      = s.Shade,
                Size       = s.Size,
            });
        }

        _statusLabel.Text = $"{_rows.Count} of {MaxStackers} stackers configured for Line {_lineId:00}.";
        UpdateButtonStates();
    }

    private async Task OpenEditDialogAsync(StackerRow? row)
    {
        if (row == null && _rows.Count >= MaxStackers)
        {
            MessageBox.Show($"Cannot define more than {MaxStackers} stackers for this line.",
                "Stacker Maintenance", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var stackNum = row?.StackNum ?? NextFreeStackNumber();
        using var dlg = new StackerEditDialog(_boxRepo, _doesMexico, _lineId, stackNum, row?.Record, _settings, _doesTwoPrims);
        if (dlg.ShowDialog(this) == DialogResult.OK)
            await ReloadAsync();
    }

    private string NextFreeStackNumber()
    {
        var used = _rows.Select(r => r.StackNum.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i <= MaxStackers; i++)
        {
            var candidate = i.ToString();
            if (!used.Contains(candidate))
                return candidate;
        }
        return MaxStackers.ToString();
    }

    private async Task DeleteSelectedAsync()
    {
        var row = SelectedRow();
        if (row == null) return;

        var confirm = MessageBox.Show(
            $"Delete stacker {row.StackNum} ({row.ItemNumber})?",
            "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        await _boxRepo.DeleteStackerAsync(_lineId, row.StackNum, CancellationToken.None);
        await ReloadAsync();
    }

    private sealed class StackerRow
    {
        public StackerRecord Record { get; set; } = null!;
        public string StackNum { get; set; } = string.Empty;
        public string ItemNumber { get; set; } = string.Empty;
        public int Qty { get; set; }
        public int Shade { get; set; }
        public string Size { get; set; } = string.Empty;
    }
}

/// <summary>Add/Edit panel for a single stacker — item lookup, shade, size.</summary>
internal sealed class StackerEditDialog : Form
{
    private readonly IBoxRepository _boxRepo;
    private readonly bool _doesMexico;
    private readonly int _lineId;
    private readonly string _stackNum;
    private readonly StackerRecord? _existing;
    private readonly AppSettings _settings;
    private readonly bool _doesTwoPrims;

    private TextBox _itemNumberInput = null!;
    private TextBox _shadeInput = null!;
    private TextBox _sizeInput = null!;
    private Label _descLabel = null!;
    private Label _qtyLabel = null!;
    private Label _errorLabel = null!;

    private ItemDetail? _resolvedItem;

    public StackerEditDialog(IBoxRepository boxRepo, bool doesMexico, int lineId, string stackNum, StackerRecord? existing, AppSettings settings, bool doesTwoPrims)
    {
        _boxRepo = boxRepo;
        _doesMexico = doesMexico;
        _lineId = lineId;
        _stackNum = stackNum;
        _existing = existing;
        _settings = settings;
        _doesTwoPrims = doesTwoPrims;

        Text = existing == null ? $"Add Stacker {stackNum}" : $"Edit Stacker {stackNum}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 300);
        BackColor = Color.MidnightBlue;
        ForeColor = Color.White;

        BuildUI();
        Load += async (_, _) => await InitializeAsync();
    }

    private void BuildUI()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6,
            Padding = new Padding(20, 16, 20, 12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        Label Lbl(string text) => new() { Text = text, Font = new Font("Segoe UI", 10f), ForeColor = Color.LightGray, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

        layout.Controls.Add(Lbl("Stacker#:"), 0, 0);
        layout.Controls.Add(new Label { Text = _stackNum, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = Color.White, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 0);

        layout.Controls.Add(Lbl("Item Number:"), 0, 1);
        _itemNumberInput = new TextBox { Dock = DockStyle.Fill, MaxLength = 15, Font = new Font("Segoe UI", 10f) };
        _itemNumberInput.Leave += async (_, _) => await ResolveItemAsync();
        layout.Controls.Add(_itemNumberInput, 1, 1);

        layout.Controls.Add(Lbl("Description:"), 0, 2);
        _descLabel = new Label { Dock = DockStyle.Fill, ForeColor = Color.LightCyan, Font = new Font("Segoe UI", 8.5f), AutoEllipsis = true };
        layout.Controls.Add(_descLabel, 1, 2);

        layout.Controls.Add(Lbl("Qty (Lis Qty):"), 0, 3);
        _qtyLabel = new Label { Dock = DockStyle.Fill, ForeColor = Color.LightCyan, Font = new Font("Segoe UI", 10f) };
        layout.Controls.Add(_qtyLabel, 1, 3);

        layout.Controls.Add(Lbl("Shade:"), 0, 4);
        _shadeInput = new TextBox { Dock = DockStyle.Fill, MaxLength = 4, Font = new Font("Segoe UI", 10f) };
        layout.Controls.Add(_shadeInput, 1, 4);

        layout.Controls.Add(Lbl("Size (0-9):"), 0, 5);
        _sizeInput = new TextBox { Dock = DockStyle.Fill, MaxLength = 1, Font = new Font("Segoe UI", 10f) };
        layout.Controls.Add(_sizeInput, 1, 5);

        Controls.Add(layout);

        _errorLabel = new Label
        {
            Dock = DockStyle.Bottom, Height = 26, ForeColor = Color.Salmon,
            Font = new Font("Segoe UI", 9f), TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(20, 0, 20, 0),
        };
        Controls.Add(_errorLabel);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 52, Padding = new Padding(16, 8, 16, 8), BackColor = Color.DarkSlateGray };
        var btnSave = new Button { Text = "Save", Size = new Size(110, 36), BackColor = Color.DarkGreen, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        btnSave.Click += async (_, _) => await SaveAsync();
        var btnCancel = new Button { Text = "Cancel", Size = new Size(90, 36), BackColor = Color.DarkRed, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10f), DialogResult = DialogResult.Cancel };
        btnPanel.Controls.Add(btnSave);
        btnPanel.Controls.Add(btnCancel);
        Controls.Add(btnPanel);

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    private async Task InitializeAsync()
    {
        if (_existing != null && !string.IsNullOrWhiteSpace(_existing.ItemNumber))
        {
            var item = await _boxRepo.GetItemDetailByNumberAsync(_existing.ItemNumber, _doesMexico, CancellationToken.None);
            if (item != null)
            {
                _resolvedItem = item;
                _itemNumberInput.Text = item.ItemNumber;
                _descLabel.Text = item.GetPrimaryItemDescription();
                _qtyLabel.Text = item.LisQty.ToString();
            }
        }

        _shadeInput.Text = _existing?.Shade.ToString() ?? string.Empty;
        _sizeInput.Text = _existing?.Size ?? string.Empty;
        _itemNumberInput.Focus();
    }

    private async Task ResolveItemAsync()
    {
        var typed = _itemNumberInput.Text.Trim();
        _errorLabel.Text = string.Empty;

        if (string.IsNullOrEmpty(typed))
        {
            _resolvedItem = null;
            _descLabel.Text = string.Empty;
            _qtyLabel.Text = string.Empty;
            return;
        }

        // Already resolved to this exact item — nothing to do.
        if (_resolvedItem != null && string.Equals(_resolvedItem.ItemNumber, typed, StringComparison.OrdinalIgnoreCase))
            return;

        // A stacker can only be assigned to the line's currently-configured Primary or
        // Secondary item (Settings) — each stacker is a shade/size variant of one of those
        // two, never an arbitrary item from the full master.
        var primary = _settings.CurrentPrimaryItem?.Trim() ?? string.Empty;
        var secondary = _doesTwoPrims ? _settings.CurrentSecondaryItem?.Trim() ?? string.Empty : string.Empty;
        var isAllowed = (!string.IsNullOrEmpty(primary) && string.Equals(typed, primary, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(secondary) && string.Equals(typed, secondary, StringComparison.OrdinalIgnoreCase));

        if (!isAllowed)
        {
            _resolvedItem = null;
            _descLabel.Text = string.Empty;
            _qtyLabel.Text = string.Empty;
            _errorLabel.Text = string.IsNullOrEmpty(primary) && string.IsNullOrEmpty(secondary)
                ? "No Primary/Secondary Item is configured for this line yet — set it in Settings first."
                : $"Only the line's Primary Item ({primary}){(string.IsNullOrEmpty(secondary) ? "" : $" or Secondary Item ({secondary})")} can be assigned to a stacker.";
            return;
        }

        var matches = await _boxRepo.GetAllItemDetailsByNumberAsync(typed, _doesMexico, CancellationToken.None);

        if (matches.Count == 0)
        {
            _resolvedItem = null;
            _descLabel.Text = string.Empty;
            _qtyLabel.Text = string.Empty;
            _errorLabel.Text = $"Item '{typed}' not found.";
            return;
        }

        if (matches.Count == 1)
        {
            _resolvedItem = matches[0];
        }
        else
        {
            using var picker = new ItemVariantPickerDialog(matches);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected == null)
            {
                _resolvedItem = null;
                _descLabel.Text = string.Empty;
                _qtyLabel.Text = string.Empty;
                _errorLabel.Text = "No Lis Qty variant selected.";
                return;
            }
            _resolvedItem = picker.Selected;
            _itemNumberInput.Text = _resolvedItem.ItemNumber;
        }

        _descLabel.Text = _resolvedItem.GetPrimaryItemDescription();
        _qtyLabel.Text = _resolvedItem.LisQty.ToString();
    }

    private async Task SaveAsync()
    {
        if (_resolvedItem == null)
            await ResolveItemAsync();

        if (_resolvedItem == null)
        {
            _errorLabel.Text = "Enter a valid item number before saving.";
            return;
        }

        var sizeText = _sizeInput.Text.Trim();
        if (sizeText.Length != 1 || !char.IsDigit(sizeText[0]))
        {
            _errorLabel.Text = "Size must be a single digit 0-9.";
            return;
        }

        var shadeText = _shadeInput.Text.Trim();
        if (shadeText.Length > 0 && !int.TryParse(shadeText, out _))
        {
            _errorLabel.Text = "Shade must be a number (or left blank).";
            return;
        }

        var record = new StackerRecord
        {
            LineId     = _lineId,
            StackNum   = _stackNum,
            ItemNumber = _resolvedItem.ItemNumber,
            Qty        = _resolvedItem.LisQty,
            Shade      = shadeText.Length > 0 ? int.Parse(shadeText) : 0,
            Size       = sizeText,
        };

        await _boxRepo.SaveStackerAsync(record, CancellationToken.None);
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>Small picker shown when an item number resolves to more than one Lis Qty variant.</summary>
internal sealed class ItemVariantPickerDialog : Form
{
    private readonly ListBox _list;
    private readonly List<ItemDetail> _items;

    public ItemDetail? Selected { get; private set; }

    public ItemVariantPickerDialog(List<ItemDetail> items)
    {
        _items = items;

        Text = "Select Lis Qty";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 260);
        BackColor = Color.MidnightBlue;
        ForeColor = Color.White;

        var label = new Label
        {
            Text = "This item number has multiple Lis Qty variants — pick one:",
            Dock = DockStyle.Top, Height = 30, Padding = new Padding(12, 8, 12, 0),
            ForeColor = Color.LightGray, Font = new Font("Segoe UI", 9.5f),
        };
        Controls.Add(label);

        _list = new ListBox
        {
            Dock = DockStyle.Fill, Font = new Font("Courier New", 10f),
            BackColor = Color.DarkSlateGray, ForeColor = Color.White,
        };
        foreach (var item in items)
            _list.Items.Add($"{item.ItemNumber,-16} Qty:{item.LisQty,-6} {item.GetPrimaryItemDescription()}");
        _list.DoubleClick += (_, _) => Accept();
        Controls.Add(_list);
        _list.BringToFront();

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 52, Padding = new Padding(12, 8, 12, 8), BackColor = Color.DarkSlateGray };
        var btnOk = new Button { Text = "Select", Size = new Size(100, 36), BackColor = Color.DarkGreen, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnOk.Click += (_, _) => Accept();
        var btnCancel = new Button { Text = "Cancel", Size = new Size(90, 36), BackColor = Color.DarkRed, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel };
        btnPanel.Controls.Add(btnOk);
        btnPanel.Controls.Add(btnCancel);
        Controls.Add(btnPanel);

        CancelButton = btnCancel;
        if (_list.Items.Count > 0)
            _list.SelectedIndex = 0;
    }

    private void Accept()
    {
        if (_list.SelectedIndex < 0) return;
        Selected = _items[_list.SelectedIndex];
        DialogResult = DialogResult.OK;
        Close();
    }
}
