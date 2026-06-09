using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace XmlCopiadorApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

public sealed class XmlCopyConfig
{
    public string DestinationBase { get; set; } = "";
    public string Action { get; set; } = "MOVE";
    public bool CreateFolders { get; set; } = true;
    public int RetryCount { get; set; } = 2;
    public int RetryDelaySeconds { get; set; } = 2;
    public bool GenerateXml { get; set; }
    public bool CustomPeriod { get; set; }
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
    public Dictionary<string, string> MonthFolders { get; set; } = new();
    public List<CompanyConfig> Companies { get; set; } = new();
    public List<DatabaseConnectionConfig> DatabaseConnections { get; set; } = new();
    public UpdateSettings Update { get; set; } = new();
}

public sealed class UpdateSettings
{
    public bool Enabled { get; set; }
    public string Manifest { get; set; } = "";
}

public sealed class UpdateManifest
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

public sealed class CompanyConfig
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "";
    public string Cnpj { get; set; } = "";
    public string Uf { get; set; } = "22";
    public string OriginPath { get; set; } = "";
}

public sealed class DatabaseConnectionConfig
{
    public bool Enabled { get; set; } = true;
    public string Server { get; set; } = "";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string DestinationPath { get; set; } = "";
}

public sealed class MonthOption
{
    public int Number { get; init; }
    public string Name { get; init; } = "";

    public override string ToString() => $"{Number:00} - {Name}";
}

public sealed class StateOption
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";

    public string Label => $"{Name} ({Code})";
}

internal static class AppTheme
{
    public static readonly Color WindowBackground = Color.FromArgb(246, 242, 251);
    public static readonly Color Surface = Color.FromArgb(253, 251, 255);
    public static readonly Color SurfaceAlt = Color.FromArgb(241, 235, 249);
    public static readonly Color InputBackground = Color.FromArgb(255, 255, 255);
    public static readonly Color Primary = Color.FromArgb(103, 58, 183);
    public static readonly Color PrimaryDark = Color.FromArgb(69, 39, 160);
    public static readonly Color PrimaryMuted = Color.FromArgb(126, 87, 194);
    public static readonly Color PrimaryLight = Color.FromArgb(225, 214, 243);
    public static readonly Color Border = Color.FromArgb(208, 194, 226);
    public static readonly Color Text = Color.FromArgb(38, 33, 46);
    public static readonly Color TextMuted = Color.FromArgb(91, 82, 105);
    public static readonly Color LogBackground = Color.FromArgb(35, 29, 48);
    public static readonly Color LogText = Color.FromArgb(238, 232, 246);
}

public sealed class MainForm : Form
{
    private const string ExportFunctionFileName = "função exportar_xml_nfe.sql";
    private const string ExportFunctionResourceName = "Sql.exportar_xml_nfe.sql";
    private static readonly HttpClient UpdateHttpClient = new();

    private readonly string _baseDir = AppContext.BaseDirectory;
    private readonly string _assetsDir;
    private readonly string _configPath;
    private readonly string _logDir;
    private readonly object _logLock = new();
    private readonly TaskbarProgress _taskbarProgress = new();

    private XmlCopyConfig _config = new();
    private BindingList<CompanyConfig> _companies = new();
    private BindingList<DatabaseConnectionConfig> _databaseConnections = new();
    private CancellationTokenSource? _cts;
    private string _currentLogFile = "";
    private Icon? _appIcon;
    private Icon? _progressIcon;
    private Image? _logoImage;
    private int _progressClearVersion;
    private int _lastIconProgressPercent = -1;

    private readonly TextBox _txtDestination = new();
    private readonly ComboBox _cmbMonth = new();
    private readonly NumericUpDown _numYear = new();
    private readonly ComboBox _cmbAction = new();
    private readonly CheckBox _chkSimulate = new();
    private readonly CheckBox _chkCreateFolders = new();
    private readonly CheckBox _chkGenerateXml = new();
    private readonly CheckBox _chkCustomPeriod = new();
    private readonly DateTimePicker _dtPeriodStart = new();
    private readonly DateTimePicker _dtPeriodEnd = new();
    private readonly NumericUpDown _numRetry = new();
    private readonly NumericUpDown _numDelay = new();
    private readonly DataGridView _grid = new();
    private readonly DataGridView _databaseGrid = new();
    private readonly TextBox _txtLog = new();
    private readonly PurpleProgressBar _progress = new();
    private readonly Label _lblStatus = new();
    private readonly Button _btnStart = new();
    private readonly Button _btnStop = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnBrowseDestination = new();
    private readonly Button _btnAdd = new();
    private readonly Button _btnRemove = new();
    private readonly Button _btnBrowseOrigin = new();
    private readonly Button _btnLogs = new();
    private readonly Button _btnAddDatabase = new();
    private readonly Button _btnEditDatabase = new();
    private readonly Button _btnRemoveDatabase = new();
    private TableLayoutPanel? _companiesLayout;
    private GroupBox? _databaseGroup;
    private bool _updatingPeriodControls;

    public MainForm()
    {
        _assetsDir = Path.Combine(_baseDir, "Assets");
        _configPath = Path.Combine(_baseDir, "XmlCopiador.config.json");
        _logDir = Path.Combine(_baseDir, "logs");

        Text = $"Copiador de XML iCompany v{GetAppVersionText()}";
        Width = 1180;
        Height = 760;
        MinimumSize = new Size(980, 640);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;

        LoadAssets();
        BuildUi();
        ApplyTheme();
        LoadConfig();
        BindConfigToUi();
        Shown += async (_, _) => await CheckForUpdatesOnStartupAsync();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _taskbarProgress.SetState(Handle, TaskbarProgressState.NoProgress);
        RestoreBaseIcon();
        base.OnFormClosed(e);
        _appIcon?.Dispose();
        _logoImage?.Dispose();
    }

    private void BuildUi()
    {
        Font = new Font("Segoe UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        Controls.Add(root);

        root.Controls.Add(BuildGeneralGroup(), 0, 0);
        root.Controls.Add(BuildCompaniesGroup(), 0, 1);
        root.Controls.Add(BuildLogGroup(), 0, 2);
        root.Controls.Add(BuildBottomBar(), 0, 3);
    }

    private Control BuildGeneralGroup()
    {
        var group = new GroupBox { Text = "Configuracao da captura", Dock = DockStyle.Fill };
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            ColumnCount = 2,
            RowCount = 1
        };
        container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        container.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        group.Controls.Add(container);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0),
            ColumnCount = 9,
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        container.Controls.Add(layout, 0, 0);

        if (_logoImage is not null)
        {
            container.Controls.Add(new PictureBox
            {
                Dock = DockStyle.Fill,
                Image = _logoImage,
                Margin = new Padding(12, 0, 14, 32),
                SizeMode = PictureBoxSizeMode.Zoom
            }, 1, 0);
        }

        _txtDestination.Dock = DockStyle.Fill;
        _btnBrowseDestination.Text = "Procurar";
        _btnBrowseDestination.Dock = DockStyle.Fill;
        _btnBrowseDestination.Click += (_, _) => BrowseDestination();

        _cmbMonth.Dock = DockStyle.Fill;
        _cmbMonth.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbMonth.SelectedIndexChanged += (_, _) => ApplySelectedPeriodDefaults();
        _cmbMonth.Items.AddRange(new object[]
        {
            new MonthOption { Number = 1, Name = "Janeiro" },
            new MonthOption { Number = 2, Name = "Fevereiro" },
            new MonthOption { Number = 3, Name = "Marco" },
            new MonthOption { Number = 4, Name = "Abril" },
            new MonthOption { Number = 5, Name = "Maio" },
            new MonthOption { Number = 6, Name = "Junho" },
            new MonthOption { Number = 7, Name = "Julho" },
            new MonthOption { Number = 8, Name = "Agosto" },
            new MonthOption { Number = 9, Name = "Setembro" },
            new MonthOption { Number = 10, Name = "Outubro" },
            new MonthOption { Number = 11, Name = "Novembro" },
            new MonthOption { Number = 12, Name = "Dezembro" }
        });

        _numYear.Minimum = 2000;
        _numYear.Maximum = 2099;
        _numYear.Value = DateTime.Today.Year;
        _numYear.Dock = DockStyle.Fill;
        _numYear.ValueChanged += (_, _) => ApplySelectedPeriodDefaults();

        _cmbAction.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbAction.Items.AddRange(new object[] { "MOVE", "COPY" });
        _cmbAction.Dock = DockStyle.Fill;

        _chkSimulate.Text = "Simular";
        _chkSimulate.Dock = DockStyle.Fill;

        _chkCreateFolders.Text = "Criar pastas";
        _chkCreateFolders.Dock = DockStyle.Fill;

        _chkGenerateXml.Text = "Gerar XML";
        _chkGenerateXml.Dock = DockStyle.Fill;
        _chkGenerateXml.CheckedChanged += (_, _) =>
        {
            UpdateDatabaseSectionVisibility();
            UpdatePeriodDateFields();
        };

        _chkCustomPeriod.Text = "Período personalizado";
        _chkCustomPeriod.Dock = DockStyle.Fill;
        _chkCustomPeriod.CheckedChanged += (_, _) => UpdatePeriodDateFields();

        foreach (var picker in new[] { _dtPeriodStart, _dtPeriodEnd })
        {
            picker.Dock = DockStyle.Fill;
            picker.Format = DateTimePickerFormat.Custom;
            picker.CustomFormat = "dd/MM/yyyy";
            picker.Visible = false;
        }

        _dtPeriodStart.ValueChanged += (_, _) => NormalizePeriodPickerValues();
        _dtPeriodEnd.ValueChanged += (_, _) => NormalizePeriodPickerValues();

        _numRetry.Minimum = 0;
        _numRetry.Maximum = 20;
        _numRetry.Dock = DockStyle.Fill;

        _numDelay.Minimum = 0;
        _numDelay.Maximum = 60;
        _numDelay.Dock = DockStyle.Fill;

        layout.Controls.Add(new Label { Text = "Destino", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
        layout.Controls.Add(_txtDestination, 1, 0);
        layout.SetColumnSpan(_txtDestination, 5);
        layout.Controls.Add(_btnBrowseDestination, 6, 0);
        layout.SetColumnSpan(_btnBrowseDestination, 2);

        layout.Controls.Add(new Label { Text = "Mes", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 1);
        layout.Controls.Add(_cmbMonth, 1, 1);
        layout.Controls.Add(new Label { Text = "Ano", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 2, 1);
        layout.Controls.Add(_numYear, 3, 1);
        layout.Controls.Add(new Label { Text = "Acao", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 4, 1);
        layout.Controls.Add(_cmbAction, 5, 1);
        layout.Controls.Add(_chkSimulate, 6, 1);
        layout.Controls.Add(_chkCreateFolders, 7, 1);

        layout.Controls.Add(new Label { Text = "Tentativas", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 2);
        layout.Controls.Add(_numRetry, 1, 2);
        layout.Controls.Add(new Label { Text = "Espera", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 2, 2);
        layout.Controls.Add(_numDelay, 3, 2);
        layout.Controls.Add(new Label { Text = "segundos", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 4, 2);
        layout.Controls.Add(_chkGenerateXml, 5, 2);
        layout.Controls.Add(_chkCustomPeriod, 6, 2);
        layout.Controls.Add(_dtPeriodStart, 7, 2);
        layout.Controls.Add(_dtPeriodEnd, 8, 2);

        return group;
    }

    private Control BuildCompaniesGroup()
    {
        var group = new GroupBox { Text = "Empresas", Dock = DockStyle.Fill };
        _companiesLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        _companiesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _companiesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        _companiesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        group.Controls.Add(_companiesLayout);

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowHeadersWidth = 28;
        _grid.DataError += (_, e) =>
        {
            e.ThrowException = false;
        };
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Ativa", DataPropertyName = nameof(CompanyConfig.Enabled), Width = 58, FillWeight = 8 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Nome", DataPropertyName = nameof(CompanyConfig.Name), FillWeight = 22 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "CNPJ", DataPropertyName = nameof(CompanyConfig.Cnpj), FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            HeaderText = "Estado",
            DataPropertyName = nameof(CompanyConfig.Uf),
            DataSource = StateOptions(),
            DisplayMember = nameof(StateOption.Label),
            ValueMember = nameof(StateOption.Code),
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            FlatStyle = FlatStyle.Flat,
            FillWeight = 16
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Origem dos XMLs", DataPropertyName = nameof(CompanyConfig.OriginPath), FillWeight = 50 });
        _companiesLayout.Controls.Add(_grid, 0, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        _btnAdd.Text = "Adicionar";
        _btnRemove.Text = "Remover";
        _btnBrowseOrigin.Text = "Escolher origem";
        _btnSave.Text = "Salvar configuracao";
        _btnLogs.Text = "Abrir logs";

        foreach (var button in new[] { _btnAdd, _btnRemove, _btnBrowseOrigin, _btnSave, _btnLogs })
        {
            button.Width = button == _btnSave ? 170 : button == _btnBrowseOrigin ? 150 : 120;
            button.Height = 28;
            buttons.Controls.Add(button);
        }

        _btnAdd.Click += (_, _) => AddCompany();
        _btnRemove.Click += (_, _) => RemoveCompany();
        _btnBrowseOrigin.Click += (_, _) => BrowseOrigin();
        _btnSave.Click += (_, _) => SaveConfigFromUi(showMessage: true);
        _btnLogs.Click += (_, _) => OpenLogsFolder();

        _companiesLayout.Controls.Add(buttons, 0, 1);
        _companiesLayout.Controls.Add(BuildDatabaseConnectionsGroup(), 0, 2);
        return group;
    }

    private Control BuildDatabaseConnectionsGroup()
    {
        _databaseGroup = new GroupBox { Text = "Conexões PostgreSQL para gerar XML", Dock = DockStyle.Fill, Visible = false };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        _databaseGroup.Controls.Add(layout);

        _databaseGrid.Dock = DockStyle.Fill;
        _databaseGrid.AutoGenerateColumns = false;
        _databaseGrid.AllowUserToAddRows = false;
        _databaseGrid.AllowUserToDeleteRows = false;
        _databaseGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _databaseGrid.MultiSelect = false;
        _databaseGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _databaseGrid.RowHeadersWidth = 28;
        _databaseGrid.DataError += (_, e) =>
        {
            e.ThrowException = false;
        };
        _databaseGrid.CellDoubleClick += (_, _) => EditDatabaseConnection();
        _databaseGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Ativa", DataPropertyName = nameof(DatabaseConnectionConfig.Enabled), Width = 58, FillWeight = 8 });
        _databaseGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Servidor", DataPropertyName = nameof(DatabaseConnectionConfig.Server), FillWeight = 22 });
        _databaseGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Porta", DataPropertyName = nameof(DatabaseConnectionConfig.Port), FillWeight = 8 });
        _databaseGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Banco de dados", DataPropertyName = nameof(DatabaseConnectionConfig.Database), FillWeight = 18 });
        _databaseGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Usuário", DataPropertyName = nameof(DatabaseConnectionConfig.Username), FillWeight = 14 });
        _databaseGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Caminho de destino", DataPropertyName = nameof(DatabaseConnectionConfig.DestinationPath), FillWeight = 30 });
        layout.Controls.Add(_databaseGrid, 0, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        _btnAddDatabase.Text = "Adicionar conexão";
        _btnEditDatabase.Text = "Editar";
        _btnRemoveDatabase.Text = "Remover";

        foreach (var button in new[] { _btnAddDatabase, _btnEditDatabase, _btnRemoveDatabase })
        {
            button.Width = button == _btnAddDatabase ? 160 : 120;
            button.Height = 28;
            buttons.Controls.Add(button);
        }

        _btnAddDatabase.Click += (_, _) => AddDatabaseConnection();
        _btnEditDatabase.Click += (_, _) => EditDatabaseConnection();
        _btnRemoveDatabase.Click += (_, _) => RemoveDatabaseConnection();

        layout.Controls.Add(buttons, 0, 1);
        return _databaseGroup;
    }

    private Control BuildLogGroup()
    {
        var group = new GroupBox { Text = "Execucao", Dock = DockStyle.Fill };
        _txtLog.Dock = DockStyle.Fill;
        _txtLog.Multiline = true;
        _txtLog.ReadOnly = true;
        _txtLog.ScrollBars = ScrollBars.Vertical;
        _txtLog.Font = new Font("Consolas", 9F);
        group.Controls.Add(_txtLog);
        return group;
    }

    private Control BuildBottomBar()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        _lblStatus.Text = "Pronto";
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.Dock = DockStyle.Fill;

        _progress.Dock = DockStyle.Fill;
        _progress.Maximum = 1;

        _btnStart.Text = "Iniciar";
        _btnStart.Dock = DockStyle.Fill;
        _btnStop.Text = "Parar";
        _btnStop.Dock = DockStyle.Fill;
        _btnStop.Enabled = false;

        _btnStart.Click += (_, _) => StartCopy();
        _btnStop.Click += (_, _) => _cts?.Cancel();

        panel.Controls.Add(_lblStatus, 0, 0);
        panel.Controls.Add(_progress, 1, 0);
        panel.Controls.Add(_btnStart, 2, 0);
        panel.Controls.Add(_btnStop, 3, 0);

        return panel;
    }

    private void ApplyTheme()
    {
        BackColor = AppTheme.WindowBackground;
        ForeColor = AppTheme.Text;
        ApplyThemeToChildren(Controls);

        StyleButton(_btnBrowseDestination, primary: false);
        StyleButton(_btnAdd, primary: false);
        StyleButton(_btnRemove, primary: false);
        StyleButton(_btnBrowseOrigin, primary: false);
        StyleButton(_btnSave, primary: true);
        StyleButton(_btnLogs, primary: false);
        StyleButton(_btnAddDatabase, primary: false);
        StyleButton(_btnEditDatabase, primary: false);
        StyleButton(_btnRemoveDatabase, primary: false);
        StyleButton(_btnStart, primary: true);
        StyleButton(_btnStop, primary: false);
        StyleGrid();

        _txtLog.BackColor = AppTheme.LogBackground;
        _txtLog.ForeColor = AppTheme.LogText;
        _txtLog.BorderStyle = BorderStyle.FixedSingle;
        _progress.BackColor = AppTheme.PrimaryLight;
        _progress.ForeColor = AppTheme.Primary;
    }

    private void ApplyThemeToChildren(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            switch (control)
            {
                case GroupBox group:
                    group.BackColor = AppTheme.Surface;
                    group.ForeColor = AppTheme.PrimaryDark;
                    break;
                case TableLayoutPanel panel:
                    panel.BackColor = panel.Parent is Form ? AppTheme.WindowBackground : AppTheme.Surface;
                    break;
                case FlowLayoutPanel panel:
                    panel.BackColor = AppTheme.Surface;
                    break;
                case Label label:
                    label.ForeColor = AppTheme.TextMuted;
                    label.BackColor = Color.Transparent;
                    break;
                case CheckBox checkBox:
                    checkBox.ForeColor = AppTheme.Text;
                    checkBox.BackColor = AppTheme.Surface;
                    break;
                case TextBox textBox:
                    textBox.BackColor = AppTheme.InputBackground;
                    textBox.ForeColor = AppTheme.Text;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ComboBox comboBox:
                    comboBox.BackColor = AppTheme.InputBackground;
                    comboBox.ForeColor = AppTheme.Text;
                    break;
                case NumericUpDown number:
                    number.BackColor = AppTheme.InputBackground;
                    number.ForeColor = AppTheme.Text;
                    break;
                case DateTimePicker picker:
                    picker.BackColor = AppTheme.InputBackground;
                    picker.ForeColor = AppTheme.Text;
                    break;
            }

            if (control.Controls.Count > 0)
            {
                ApplyThemeToChildren(control.Controls);
            }
        }
    }

    private static void StyleButton(Button button, bool primary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = primary ? AppTheme.PrimaryDark : AppTheme.PrimaryLight;
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(49, 27, 146) : Color.FromArgb(211, 198, 232);

        if (primary)
        {
            button.BackColor = AppTheme.Primary;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderColor = AppTheme.PrimaryDark;
        }
        else
        {
            button.BackColor = AppTheme.SurfaceAlt;
            button.ForeColor = AppTheme.PrimaryDark;
            button.FlatAppearance.BorderColor = AppTheme.Border;
        }
    }

    private void StyleGrid()
    {
        StyleDataGrid(_grid);
        StyleDataGrid(_databaseGrid);
    }

    private void StyleDataGrid(DataGridView grid)
    {
        grid.BackgroundColor = AppTheme.Surface;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.GridColor = AppTheme.Border;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = AppTheme.Primary;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = AppTheme.PrimaryDark;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
        grid.RowHeadersDefaultCellStyle.BackColor = AppTheme.SurfaceAlt;
        grid.RowHeadersDefaultCellStyle.ForeColor = AppTheme.TextMuted;
        grid.DefaultCellStyle.BackColor = AppTheme.InputBackground;
        grid.DefaultCellStyle.ForeColor = AppTheme.Text;
        grid.DefaultCellStyle.SelectionBackColor = AppTheme.PrimaryLight;
        grid.DefaultCellStyle.SelectionForeColor = AppTheme.Text;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 247, 254);
    }

    private void LoadAssets()
    {
        _appIcon = LoadIconAsset("ic_launcher.ico", "Assets.ic_launcher.ico");
        if (_appIcon is not null)
        {
            Icon = _appIcon;
        }

        _logoImage = LoadImageAsset("Logo Sem Fundo_3.png", "Assets.LogoSemFundo3.png");
    }

    private Icon? LoadIconAsset(string fileName, string resourceName)
    {
        var iconPath = FindAsset(fileName);
        if (File.Exists(iconPath))
        {
            using var icon = new Icon(iconPath);
            return (Icon)icon.Clone();
        }

        using var stream = typeof(Program).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var embeddedIcon = new Icon(stream);
        return (Icon)embeddedIcon.Clone();
    }

    private Image? LoadImageAsset(string fileName, string resourceName)
    {
        var logoPath = FindAsset(fileName);
        if (File.Exists(logoPath))
        {
            return Image.FromFile(logoPath);
        }

        using var stream = typeof(Program).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private string FindAsset(string fileName)
    {
        var publishedPath = Path.Combine(_assetsDir, fileName);
        if (File.Exists(publishedPath))
        {
            return publishedPath;
        }

        return Path.GetFullPath(Path.Combine(_baseDir, "..", "..", "..", "..", "Assets", fileName));
    }

    private void LoadConfig()
    {
        if (!File.Exists(_configPath))
        {
            _config = CreateNewConfig();
            return;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            _config = JsonSerializer.Deserialize<XmlCopyConfig>(json, JsonOptions()) ?? CreateNewConfig();
            EnsureConfigDefaults(_config);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Nao consegui ler a configuracao.\n\n{ex.Message}\n\nVou carregar uma configuracao nova e vazia.", "Configuracao", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _config = CreateNewConfig();
        }
    }

    private void BindConfigToUi()
    {
        _updatingPeriodControls = true;
        _txtDestination.Text = _config.DestinationBase;
        _cmbAction.SelectedItem = string.Equals(_config.Action, "COPY", StringComparison.OrdinalIgnoreCase) ? "COPY" : "MOVE";
        _chkCreateFolders.Checked = _config.CreateFolders;
        _chkGenerateXml.Checked = _config.GenerateXml;
        _numRetry.Value = Math.Clamp(_config.RetryCount, 0, 20);
        _numDelay.Value = Math.Clamp(_config.RetryDelaySeconds, 0, 60);

        var defaultPeriod = DateTime.Today.AddMonths(-1);
        _numYear.Value = defaultPeriod.Year;

        for (var i = 0; i < _cmbMonth.Items.Count; i++)
        {
            if (_cmbMonth.Items[i] is MonthOption option && option.Number == defaultPeriod.Month)
            {
                _cmbMonth.SelectedIndex = i;
                break;
            }
        }

        BindPeriodConfigToUi(defaultPeriod.Month, defaultPeriod.Year);
        _updatingPeriodControls = false;
        UpdatePeriodDateFields();

        _companies = new BindingList<CompanyConfig>(_config.Companies);
        _grid.DataSource = _companies;
        _databaseConnections = new BindingList<DatabaseConnectionConfig>(_config.DatabaseConnections);
        _databaseGrid.DataSource = _databaseConnections;
        UpdateDatabaseSectionVisibility();
    }

    private void SaveConfigFromUi(bool showMessage)
    {
        _grid.EndEdit();
        _databaseGrid.EndEdit();

        _config.DestinationBase = _txtDestination.Text.Trim();
        _config.Action = (_cmbAction.SelectedItem?.ToString() ?? "MOVE").ToUpperInvariant();
        _config.CreateFolders = _chkCreateFolders.Checked;
        _config.GenerateXml = _chkGenerateXml.Checked;
        _config.CustomPeriod = _chkCustomPeriod.Checked;
        _config.PeriodStart = _dtPeriodStart.Value.Date;
        _config.PeriodEnd = _dtPeriodEnd.Value.Date;
        _config.RetryCount = (int)_numRetry.Value;
        _config.RetryDelaySeconds = (int)_numDelay.Value;
        _config.Companies = _companies.ToList();
        _config.DatabaseConnections = _databaseConnections.ToList();
        EnsureConfigDefaults(_config);
        SaveConfig(_config);

        if (showMessage)
        {
            MessageBox.Show("Configuracao salva.", "Copiador de XML", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void SaveConfig(XmlCopyConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        File.WriteAllText(_configPath, JsonSerializer.Serialize(config, JsonOptions()));
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static XmlCopyConfig CreateNewConfig()
    {
        var config = new XmlCopyConfig
        {
            DestinationBase = "",
            Action = "MOVE",
            CreateFolders = true,
            RetryCount = 2,
            RetryDelaySeconds = 2,
            GenerateXml = false,
            CustomPeriod = false,
            Companies = new List<CompanyConfig>(),
            DatabaseConnections = new List<DatabaseConnectionConfig>(),
            Update = new UpdateSettings()
        };

        EnsureConfigDefaults(config);
        return config;
    }

    private static void EnsureConfigDefaults(XmlCopyConfig config)
    {
        config.MonthFolders ??= new Dictionary<string, string>();
        var defaults = new Dictionary<string, string>
        {
            ["01"] = "01 - Janeiro",
            ["02"] = "02 - Fevereiro",
            ["03"] = "03 - Marco",
            ["04"] = "04 - Abril",
            ["05"] = "05 - Maio",
            ["06"] = "06 - Junho",
            ["07"] = "07 - Julho",
            ["08"] = "08 - Agosto",
            ["09"] = "09 - Setembro",
            ["10"] = "10 - Outubro",
            ["11"] = "11 - Novembro",
            ["12"] = "12 - Dezembro"
        };

        foreach (var pair in defaults)
        {
            if (!config.MonthFolders.ContainsKey(pair.Key) || string.IsNullOrWhiteSpace(config.MonthFolders[pair.Key]))
            {
                config.MonthFolders[pair.Key] = pair.Value;
            }
        }

        config.Companies ??= new List<CompanyConfig>();
        config.DatabaseConnections ??= new List<DatabaseConnectionConfig>();
        if (config.PeriodStart.HasValue)
        {
            config.PeriodStart = config.PeriodStart.Value.Date;
        }

        if (config.PeriodEnd.HasValue)
        {
            config.PeriodEnd = config.PeriodEnd.Value.Date;
        }

        config.Update ??= new UpdateSettings();
        var validStateCodes = StateOptions().Select(s => s.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var company in config.Companies)
        {
            if (!validStateCodes.Contains(company.Uf))
            {
                company.Uf = "22";
            }
        }

        foreach (var connection in config.DatabaseConnections)
        {
            connection.Port = Math.Clamp(connection.Port <= 0 ? 5432 : connection.Port, 1, 65535);
            connection.Server = connection.Server.Trim();
            connection.Database = connection.Database.Trim();
            connection.Username = connection.Username.Trim();
            connection.DestinationPath = connection.DestinationPath.Trim();
        }

        config.Action = string.Equals(config.Action, "COPY", StringComparison.OrdinalIgnoreCase) ? "COPY" : "MOVE";
        config.RetryCount = Math.Clamp(config.RetryCount, 0, 20);
        config.RetryDelaySeconds = Math.Clamp(config.RetryDelaySeconds, 0, 60);
    }

    private void BindPeriodConfigToUi(int month, int year)
    {
        var (monthStart, monthEnd) = GetMonthDateRange(month, year);
        var isCurrentMonth = IsCurrentMonth(month, year);
        var defaultEnd = isCurrentMonth ? DateTime.Today : monthEnd;

        SetDatePickerValue(_dtPeriodStart, _config.PeriodStart ?? monthStart);
        SetDatePickerValue(_dtPeriodEnd, _config.PeriodEnd ?? defaultEnd);
        _chkCustomPeriod.Checked = isCurrentMonth || _config.CustomPeriod;
        _chkCustomPeriod.Enabled = !isCurrentMonth;
        NormalizePeriodPickerValues();
    }

    private void ApplySelectedPeriodDefaults()
    {
        if (_updatingPeriodControls)
        {
            return;
        }

        var period = GetSelectedMonthDateRange();
        var isCurrentMonth = IsSelectedCurrentMonth();

        _updatingPeriodControls = true;
        try
        {
            _chkCustomPeriod.Checked = isCurrentMonth;
            _chkCustomPeriod.Enabled = !isCurrentMonth;
            SetDatePickerValue(_dtPeriodStart, period.Start);
            SetDatePickerValue(_dtPeriodEnd, isCurrentMonth ? DateTime.Today : period.End);
        }
        finally
        {
            _updatingPeriodControls = false;
        }

        UpdatePeriodDateFields();
    }

    private void UpdatePeriodDateFields()
    {
        var showDates = _chkGenerateXml.Checked && _chkCustomPeriod.Checked;
        _chkCustomPeriod.Visible = _chkGenerateXml.Checked;
        _dtPeriodStart.Visible = showDates;
        _dtPeriodEnd.Visible = showDates;
        _dtPeriodStart.Enabled = showDates;
        _dtPeriodEnd.Enabled = showDates;

        if (IsSelectedCurrentMonth())
        {
            if (!_chkCustomPeriod.Checked)
            {
                _chkCustomPeriod.Checked = true;
            }

            _chkCustomPeriod.Enabled = false;
            if (_dtPeriodEnd.Value.Date > DateTime.Today)
            {
                SetDatePickerValue(_dtPeriodEnd, DateTime.Today);
            }
        }
        else
        {
            _chkCustomPeriod.Enabled = _chkGenerateXml.Checked;
        }

        NormalizePeriodPickerValues();
    }

    private void NormalizePeriodPickerValues()
    {
        if (_updatingPeriodControls)
        {
            return;
        }

        _updatingPeriodControls = true;
        try
        {
            if (IsSelectedCurrentMonth() && _dtPeriodEnd.Value.Date > DateTime.Today)
            {
                SetDatePickerValue(_dtPeriodEnd, DateTime.Today);
            }

            if (_dtPeriodStart.Value.Date > _dtPeriodEnd.Value.Date)
            {
                SetDatePickerValue(_dtPeriodStart, _dtPeriodEnd.Value.Date);
            }
        }
        finally
        {
            _updatingPeriodControls = false;
        }
    }

    private (DateTime Start, DateTime End) GetSelectedGenerationPeriod()
    {
        var selectedPeriod = GetSelectedMonthDateRange();
        if (_chkGenerateXml.Checked && _chkCustomPeriod.Checked)
        {
            return (_dtPeriodStart.Value.Date, _dtPeriodEnd.Value.Date);
        }

        return selectedPeriod;
    }

    private (DateTime Start, DateTime End) GetSelectedMonthDateRange()
    {
        var month = (_cmbMonth.SelectedItem as MonthOption)?.Number ?? DateTime.Today.Month;
        var year = (int)_numYear.Value;
        return GetMonthDateRange(month, year);
    }

    private static (DateTime Start, DateTime End) GetMonthDateRange(int month, int year)
    {
        var start = new DateTime(year, month, 1);
        return (start, start.AddMonths(1).AddDays(-1));
    }

    private bool IsSelectedCurrentMonth()
    {
        var month = (_cmbMonth.SelectedItem as MonthOption)?.Number ?? DateTime.Today.Month;
        var year = (int)_numYear.Value;
        return IsCurrentMonth(month, year);
    }

    private static bool IsCurrentMonth(int month, int year)
    {
        var today = DateTime.Today;
        return year == today.Year && month == today.Month;
    }

    private static void SetDatePickerValue(DateTimePicker picker, DateTime value)
    {
        var date = value.Date;
        if (date < picker.MinDate)
        {
            date = picker.MinDate;
        }
        else if (date > picker.MaxDate)
        {
            date = picker.MaxDate;
        }

        picker.Value = date;
    }

    private void BrowseDestination()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Escolha a pasta base de destino",
            SelectedPath = Directory.Exists(_txtDestination.Text) ? _txtDestination.Text : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _txtDestination.Text = dialog.SelectedPath;
        }
    }

    private void BrowseOrigin()
    {
        if (_grid.CurrentRow?.DataBoundItem is not CompanyConfig company)
        {
            MessageBox.Show("Selecione uma empresa primeiro.", "Origem", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Escolha a pasta de origem dos XMLs",
            SelectedPath = Directory.Exists(company.OriginPath) ? company.OriginPath : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            company.OriginPath = dialog.SelectedPath;
            _grid.Refresh();
        }
    }

    private void AddCompany()
    {
        _companies.Add(new CompanyConfig { Enabled = true, Name = "Nova empresa", Uf = "22" });
    }

    private void RemoveCompany()
    {
        if (_grid.CurrentRow?.DataBoundItem is not CompanyConfig company)
        {
            return;
        }

        if (MessageBox.Show($"Remover a empresa '{company.Name}'?", "Empresas", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            _companies.Remove(company);
        }
    }

    private void UpdateDatabaseSectionVisibility()
    {
        if (_companiesLayout is null || _databaseGroup is null)
        {
            return;
        }

        var visible = _chkGenerateXml.Checked;
        _databaseGroup.Visible = visible;
        _companiesLayout.RowStyles[2].Height = visible ? 210 : 0;
    }

    private void AddDatabaseConnection()
    {
        using var dialog = new DatabaseConnectionDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Connection is not null)
        {
            _databaseConnections.Add(dialog.Connection);
        }
    }

    private void EditDatabaseConnection()
    {
        if (_databaseGrid.CurrentRow?.DataBoundItem is not DatabaseConnectionConfig connection)
        {
            return;
        }

        using var dialog = new DatabaseConnectionDialog(CloneDatabaseConnection(connection));
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Connection is not null)
        {
            CopyDatabaseConnection(dialog.Connection, connection);
            _databaseGrid.Refresh();
        }
    }

    private void RemoveDatabaseConnection()
    {
        if (_databaseGrid.CurrentRow?.DataBoundItem is not DatabaseConnectionConfig connection)
        {
            return;
        }

        var label = string.IsNullOrWhiteSpace(connection.Database) ? connection.Server : $"{connection.Server}/{connection.Database}";
        if (MessageBox.Show($"Remover a conexão '{label}'?", "Conexões PostgreSQL", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            _databaseConnections.Remove(connection);
        }
    }

    private static DatabaseConnectionConfig CloneDatabaseConnection(DatabaseConnectionConfig connection) => new()
    {
        Enabled = connection.Enabled,
        Server = connection.Server,
        Port = connection.Port,
        Database = connection.Database,
        Username = connection.Username,
        Password = connection.Password,
        DestinationPath = connection.DestinationPath
    };

    private static void CopyDatabaseConnection(DatabaseConnectionConfig source, DatabaseConnectionConfig target)
    {
        target.Enabled = source.Enabled;
        target.Server = source.Server;
        target.Port = source.Port;
        target.Database = source.Database;
        target.Username = source.Username;
        target.Password = source.Password;
        target.DestinationPath = source.DestinationPath;
    }

    private void OpenLogsFolder()
    {
        Directory.CreateDirectory(_logDir);
        Process.Start(new ProcessStartInfo { FileName = _logDir, UseShellExecute = true });
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        if (!_config.Update.Enabled || string.IsNullOrWhiteSpace(_config.Update.Manifest))
        {
            return;
        }

        try
        {
            SetStatus("Procurando nova atualizacao...");
            Log("[UPDATE] Procurando nova atualizacao...");
            var manifest = await LoadUpdateManifestAsync(_config.Update.Manifest);
            if (manifest is null || !IsUpdateAvailable(manifest.Version))
            {
                Log("[UPDATE] Nenhuma atualizacao encontrada.");
                SetStatus("Pronto");
                return;
            }

            if (string.IsNullOrWhiteSpace(manifest.DownloadUrl))
            {
                Log("[UPDATE] Manifesto sem DownloadUrl. Atualizacao ignorada.");
                SetStatus("Pronto");
                return;
            }

            Log($"[UPDATE] Atualizacao encontrada: {GetAppVersionText()} -> {manifest.Version}");
            ShowUpdateMessage(
                "Atualizacao encontrada",
                $"Uma nova versao foi encontrada.\n\nVersao atual: {GetAppVersionText()}\nNova versao: {manifest.Version}\n\nO download da atualizacao sera iniciado agora.");

            SetStatus($"Baixando atualizacao {manifest.Version}...");
            Log($"[UPDATE] Baixando atualizacao {manifest.Version}...");
            var updateFile = Path.Combine(Path.GetTempPath(), $"XmlCopiador_{manifest.Version}_{Guid.NewGuid():N}.exe");
            await DownloadUpdateFileAsync(manifest.DownloadUrl, _config.Update.Manifest, updateFile);

            if (!string.IsNullOrWhiteSpace(manifest.Sha256))
            {
                SetStatus("Validando atualizacao...");
                Log("[UPDATE] Validando SHA256 da atualizacao...");
                var hash = ComputeSha256(updateFile);
                if (!string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(updateFile);
                    Log("[UPDATE] Hash SHA256 divergente. Atualizacao cancelada.");
                    SetStatus("Pronto");
                    return;
                }
            }

            SetStatus("Atualizacao pronta para aplicar...");
            Log("[UPDATE] Atualizacao baixada e validada.");
            ShowUpdateMessage(
                "Atualizacao pronta",
                $"A atualizacao foi baixada com sucesso.\n\nO aplicativo sera encerrado e reaberto automaticamente na versao {manifest.Version}.");
            LaunchUpdater(updateFile);
            Close();
        }
        catch (Exception ex)
        {
            Log($"[UPDATE] Falha ao verificar atualizacao: {ex.Message}");
            SetStatus("Pronto");
        }
    }

    private void ShowUpdateMessage(string title, string message)
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
        MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task<UpdateManifest?> LoadUpdateManifestAsync(string manifestSource)
    {
        await using var stream = await OpenUpdateStreamAsync(manifestSource, baseSource: null);
        return await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, JsonOptions());
    }

    private async Task DownloadUpdateFileAsync(string downloadSource, string manifestSource, string destinationFile)
    {
        await using var input = await OpenUpdateStreamAsync(downloadSource, manifestSource);
        await using var output = File.Create(destinationFile);
        await input.CopyToAsync(output);
    }

    private static async Task<Stream> OpenUpdateStreamAsync(string source, string? baseSource)
    {
        var uri = BuildUpdateUri(source, baseSource);
        if (uri is not null)
        {
            return await UpdateHttpClient.GetStreamAsync(uri);
        }

        var path = ResolveUpdatePath(source, baseSource);
        return File.OpenRead(path);
    }

    private static Uri? BuildUpdateUri(string source, string? baseSource)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute;
        }

        if (!string.IsNullOrWhiteSpace(baseSource) &&
            Uri.TryCreate(baseSource, UriKind.Absolute, out var baseUri) &&
            (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            return new Uri(baseUri, source);
        }

        return null;
    }

    private static string ResolveUpdatePath(string source, string? baseSource)
    {
        var expanded = Environment.ExpandEnvironmentVariables(source);
        if (Path.IsPathRooted(expanded))
        {
            return expanded;
        }

        if (!string.IsNullOrWhiteSpace(baseSource) && !IsHttpSource(baseSource))
        {
            var basePath = ResolveUpdatePath(baseSource, null);
            var baseDir = Path.GetDirectoryName(basePath);
            if (!string.IsNullOrWhiteSpace(baseDir))
            {
                return Path.GetFullPath(Path.Combine(baseDir, expanded));
            }
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, expanded));
    }

    private static bool IsHttpSource(string source)
    {
        return Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static bool IsUpdateAvailable(string manifestVersion)
    {
        if (!Version.TryParse(NormalizeVersion(manifestVersion), out var latest))
        {
            return false;
        }

        var current = typeof(Program).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
        return latest > current;
    }

    private static string GetAppVersionText()
    {
        var version = typeof(Program).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
        return $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string NormalizeVersion(string version)
    {
        return string.IsNullOrWhiteSpace(version) ? "0.0.0" : version.Trim();
    }

    private static string ComputeSha256(string file)
    {
        using var stream = File.OpenRead(file);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private void LaunchUpdater(string downloadedExe)
    {
        var currentExe = Environment.ProcessPath ?? Application.ExecutablePath;
        var updaterFile = Path.Combine(Path.GetTempPath(), $"XmlCopiador_update_{Guid.NewGuid():N}.cmd");
        var script = $"""
@echo off
setlocal
set "SRC={downloadedExe}"
set "DST={currentExe}"
for /l %%i in (1,1,30) do (
    copy /y "%SRC%" "%DST%" >nul 2>nul && goto done
    timeout /t 1 /nobreak >nul
)
exit /b 1
:done
start "" "%DST%"
del "%SRC%" >nul 2>nul
del "%~f0" >nul 2>nul
""";

        File.WriteAllText(updaterFile, script, Encoding.ASCII);
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{updaterFile}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private async void StartCopy()
    {
        if (_cts is not null)
        {
            return;
        }

        SaveConfigFromUi(showMessage: false);

        if (!ValidateBeforeRun(out var message))
        {
            MessageBox.Show(message, "Validacao", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _txtLog.Clear();
        _currentLogFile = Path.Combine(_logDir, $"copiaxml_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
        Directory.CreateDirectory(_logDir);
        SetRunning(true);

        var selectedMonth = _cmbMonth.SelectedItem as MonthOption ?? new MonthOption { Number = DateTime.Today.Month, Name = "" };
        var year = (int)_numYear.Value;
        var simulate = _chkSimulate.Checked;
        var generationPeriod = GetSelectedGenerationPeriod();
        var snapshot = CloneConfig(_config);

        _cts = new CancellationTokenSource();

        try
        {
            await Task.Run(async () =>
            {
                if (snapshot.GenerateXml)
                {
                    await GenerateXmlsFromDatabasesAsync(snapshot, generationPeriod.Start, generationPeriod.End, _cts.Token);
                }

                ProcessXmls(snapshot, selectedMonth.Number, year, simulate, _cts.Token);
            });
            SetStatus(_cts.IsCancellationRequested ? "Cancelado" : "Concluido");
        }
        catch (Exception ex)
        {
            Log($"[ERRO] Falha inesperada: {ex.Message}");
            SetStatus("Finalizado com erro");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetRunning(false);
        }
    }

    private bool ValidateBeforeRun(out string message)
    {
        if (_cmbMonth.SelectedItem is not MonthOption)
        {
            message = "Escolha o mes.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(_config.DestinationBase))
        {
            message = "Informe a pasta de destino.";
            return false;
        }

        if (_config.Companies.Count(c => c.Enabled) == 0)
        {
            message = "Marque pelo menos uma empresa ativa.";
            return false;
        }

        if (_config.GenerateXml)
        {
            var activeConnections = _config.DatabaseConnections.Where(c => c.Enabled).ToList();
            if (activeConnections.Count == 0)
            {
                message = "Marque pelo menos uma conexao PostgreSQL ativa para gerar os XMLs.";
                return false;
            }

            foreach (var connection in activeConnections)
            {
                if (!ValidateDatabaseConnection(connection, out message))
                {
                    return false;
                }
            }

            var generationPeriod = GetSelectedGenerationPeriod();
            if (generationPeriod.Start > generationPeriod.End)
            {
                message = "A data inicial do periodo nao pode ser maior que a data final.";
                return false;
            }

            if (IsSelectedCurrentMonth() && generationPeriod.End > DateTime.Today)
            {
                message = "A data final do mes atual nao pode ser maior que a data de hoje.";
                return false;
            }
        }

        message = "";
        return true;
    }

    private static bool ValidateDatabaseConnection(DatabaseConnectionConfig connection, out string message)
    {
        var label = GetDatabaseLabel(connection);
        if (string.IsNullOrWhiteSpace(connection.Server))
        {
            message = $"Informe o servidor da conexão PostgreSQL '{label}'.";
            return false;
        }

        if (connection.Port is < 1 or > 65535)
        {
            message = $"Informe uma porta válida para a conexão PostgreSQL '{label}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(connection.Database))
        {
            message = $"Informe o banco de dados da conexão PostgreSQL '{label}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(connection.Username))
        {
            message = $"Informe o usuário da conexão PostgreSQL '{label}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(connection.DestinationPath))
        {
            message = $"Informe o caminho de destino da conexão PostgreSQL '{label}'.";
            return false;
        }

        if (!connection.DestinationPath.Trim().StartsWith("/", StringComparison.Ordinal))
        {
            message = $"O caminho de destino da conexão PostgreSQL '{label}' deve estar no formato Linux. Exemplo: /sist/icomp/empresa/exp";
            return false;
        }

        message = "";
        return true;
    }

    private static XmlCopyConfig CloneConfig(XmlCopyConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions());
        return JsonSerializer.Deserialize<XmlCopyConfig>(json, JsonOptions()) ?? CreateNewConfig();
    }

    private void SetRunning(bool running)
    {
        void Apply()
        {
            _btnStart.Enabled = !running;
            _btnStop.Enabled = running;
            _btnSave.Enabled = !running;
            _grid.ReadOnly = running;
            _databaseGrid.ReadOnly = running;
            _chkSimulate.Enabled = !running;
            _chkGenerateXml.Enabled = !running;
            _chkCustomPeriod.Enabled = !running && _chkGenerateXml.Checked && !IsSelectedCurrentMonth();
            _dtPeriodStart.Enabled = !running && _chkGenerateXml.Checked && _chkCustomPeriod.Checked;
            _dtPeriodEnd.Enabled = !running && _chkGenerateXml.Checked && _chkCustomPeriod.Checked;
            _btnAdd.Enabled = !running;
            _btnRemove.Enabled = !running;
            _btnBrowseOrigin.Enabled = !running;
            _btnAddDatabase.Enabled = !running;
            _btnEditDatabase.Enabled = !running;
            _btnRemoveDatabase.Enabled = !running;
            _lblStatus.Text = running ? "Executando..." : _lblStatus.Text;

            if (running)
            {
                _progressClearVersion++;
                _lastIconProgressPercent = -1;
                _taskbarProgress.SetState(Handle, TaskbarProgressState.Normal);
                _taskbarProgress.SetProgress(Handle, 0, 1);
                SetWindowProgressIcon(0, 1);
            }
            else
            {
                ScheduleProgressClear();
            }
        }

        if (InvokeRequired) BeginInvoke(Apply); else Apply();
    }

    private async Task GenerateXmlsFromDatabasesAsync(XmlCopyConfig config, DateTime periodStart, DateTime periodEnd, CancellationToken token)
    {
        var activeConnections = config.DatabaseConnections.Where(c => c.Enabled).ToList();
        if (activeConnections.Count == 0)
        {
            return;
        }

        var functionSql = LoadExportFunctionSql();
        SetStatus("Gerando XMLs no PostgreSQL...");
        Log("==========================================================");
        Log("Geracao de XMLs no PostgreSQL");
        Log("==========================================================");
        Log($"Periodo : {periodStart:dd/MM/yyyy} ate {periodEnd:dd/MM/yyyy}");
        Log($"Bancos  : {activeConnections.Count}");
        Log("==========================================================");

        for (var index = 0; index < activeConnections.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var connectionConfig = activeConnections[index];
            var label = GetDatabaseLabel(connectionConfig);
            Log("");
            Log($"Banco {index + 1}/{activeConnections.Count}: {label}");

            try
            {
                await using var connection = new NpgsqlConnection(DatabaseConnectionDialog.BuildConnectionString(connectionConfig));
                await connection.OpenAsync(token);

                if (!await ExportFunctionExistsAsync(connection, token))
                {
                    Log("  Funcao exportar_xml_nfe nao encontrada. Criando...");
                    await using var createCommand = new NpgsqlCommand(functionSql, connection) { CommandTimeout = 0 };
                    await createCommand.ExecuteNonQueryAsync(token);
                    Log("  Funcao exportar_xml_nfe criada.");
                }
                else
                {
                    Log("  Funcao exportar_xml_nfe encontrada.");
                }

                var destinationPath = connectionConfig.DestinationPath.Trim().Replace('\\', '/');
                await using var exportCommand = new NpgsqlCommand("select exportar_xml_nfe(@data_inicial, @data_final, @caminho)", connection)
                {
                    CommandTimeout = 0
                };
                exportCommand.Parameters.Add("data_inicial", NpgsqlDbType.Date).Value = periodStart.Date;
                exportCommand.Parameters.Add("data_final", NpgsqlDbType.Date).Value = periodEnd.Date;
                exportCommand.Parameters.Add("caminho", NpgsqlDbType.Text).Value = destinationPath;

                Log($"  Executando exportacao para {destinationPath}");
                Log($"  Script executado: {BuildExportFunctionCallLog(periodStart, periodEnd, destinationPath)}");
                if (!destinationPath.EndsWith("/", StringComparison.Ordinal))
                {
                    Log("  [AVISO] Caminho sem '/' no final. Pela funcao atual, os arquivos ficam com este caminho como prefixo do nome.");
                }
                var result = await exportCommand.ExecuteScalarAsync(token);
                var generatedFiles = result is null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
                Log($"  [OK] {generatedFiles} arquivo(s) gerado(s).");
            }
            catch (PostgresException ex)
            {
                throw new InvalidOperationException($"Falha ao gerar XMLs em {label}: {DatabaseConnectionDialog.FormatPostgresError(ex, connectionConfig)}", ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException($"Falha ao gerar XMLs em {label}: {DatabaseConnectionDialog.NormalizeDatabaseMessage(ex.Message)}", ex);
            }
        }

        Log("");
        Log("Geracao de XMLs concluida em todos os bancos ativos.");
    }

    private static async Task<bool> ExportFunctionExistsAsync(NpgsqlConnection connection, CancellationToken token)
    {
        const string sql = """
select exists (
    select 1
    from pg_proc p
    join pg_namespace n on n.oid = p.pronamespace
    where p.proname = 'exportar_xml_nfe'
      and p.pronargs = 3
      and p.proargtypes[0] = 'date'::regtype
      and p.proargtypes[1] = 'date'::regtype
      and p.proargtypes[2] = 'text'::regtype
      and n.nspname = any (current_schemas(true))
)
""";

        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync(token);
        return result is bool exists && exists;
    }

    private string LoadExportFunctionSql()
    {
        var candidates = new[]
        {
            Path.Combine(_baseDir, ExportFunctionFileName),
            Path.GetFullPath(Path.Combine(_baseDir, "..", "..", "..", "..", ExportFunctionFileName))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate, Encoding.UTF8);
            }
        }

        using var stream = typeof(Program).Assembly.GetManifestResourceStream(ExportFunctionResourceName);
        if (stream is not null)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        throw new FileNotFoundException($"Nao encontrei o arquivo {ExportFunctionFileName} para criar a funcao exportar_xml_nfe.");
    }

    private static string BuildExportFunctionCallLog(DateTime periodStart, DateTime periodEnd, string destinationPath)
    {
        return $"SELECT exportar_xml_nfe('{periodStart:yyyy-MM-dd}', '{periodEnd:yyyy-MM-dd}', '{EscapeSqlLiteral(destinationPath)}');";
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string GetDatabaseLabel(DatabaseConnectionConfig connection)
    {
        var server = string.IsNullOrWhiteSpace(connection.Server) ? "(sem servidor)" : connection.Server.Trim();
        var database = string.IsNullOrWhiteSpace(connection.Database) ? "(sem banco)" : connection.Database.Trim();
        return $"{server}:{connection.Port}/{database}";
    }

    private void ProcessXmls(XmlCopyConfig config, int month, int year, bool simulate, CancellationToken token)
    {
        const int progressScale = 1000;
        var monthKey = month.ToString("00");
        var monthFolder = config.MonthFolders.TryGetValue(monthKey, out var folder) ? folder : $"{monthKey} - Mes";
        var year2 = (year % 100).ToString("00");
        var enabledCompanies = config.Companies.Where(c => c.Enabled).ToList();
        var totalGroups = Math.Max(1, enabledCompanies.Count * 4);
        var totalProgress = totalGroups * progressScale;
        var completedProgress = 0;
        var foundFiles = 0;
        var copiedFiles = 0;
        var warnings = 0;
        var errors = 0;

        SetProgress(0, totalProgress);
        Log("==========================================================");
        Log("Captura de XMLs");
        Log("==========================================================");
        Log($"Mes/Ano : {monthFolder} / {year}");
        Log($"Destino : {config.DestinationBase}");
        Log($"Acao    : {config.Action}");
        Log($"Modo    : {(simulate ? "SIMULACAO" : "EXECUCAO")}");
        Log($"Log     : {_currentLogFile}");
        Log("==========================================================");

        foreach (var company in enabledCompanies)
        {
            if (token.IsCancellationRequested) break;

            Log("");
            Log($"Empresa: {company.Name} [{company.Cnpj}]");

            if (!ValidateCompany(company, out var validationError))
            {
                errors++;
                Log($"  [ERRO] {validationError}");
                completedProgress += 4 * progressScale;
                SetProgress(completedProgress, totalProgress);
                continue;
            }

            if (!Directory.Exists(company.OriginPath))
            {
                errors++;
                Log($"  [ERRO] Origem inacessivel: {company.OriginPath}");
                completedProgress += 4 * progressScale;
                SetProgress(completedProgress, totalProgress);
                continue;
            }

            foreach (var group in FileGroups())
            {
                if (token.IsCancellationRequested) break;

                var destination = Path.Combine(config.DestinationBase, $"20{year2}", monthFolder, company.Cnpj, group.DocType, group.Status);
                var mask = $"{company.Uf}{year2}{monthKey}{company.Cnpj}{group.Model}*{group.Suffix}";
                var files = FindFiles(company.OriginPath, mask, ref errors);

                if (files.Count == 0)
                {
                    warnings++;
                    Log($"  [AVISO] Nenhum arquivo para {group.DocType} {group.Status} - {mask}");
                    completedProgress += progressScale;
                    SetProgress(completedProgress, totalProgress);
                    continue;
                }

                foundFiles += files.Count;
                Log($"  {group.DocType} {group.Status}: {files.Count} arquivo(s)");

                try
                {
                    if (config.CreateFolders && !simulate)
                    {
                        Directory.CreateDirectory(destination);
                    }
                    else if (!Directory.Exists(destination) && !simulate)
                    {
                        errors++;
                        Log($"    [ERRO] Destino nao existe: {destination}");
                        completedProgress += progressScale;
                        SetProgress(completedProgress, totalProgress);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    Log($"    [ERRO] Nao consegui criar/acessar o destino: {destination}");
                    Log($"           {ex.Message}");
                    completedProgress += progressScale;
                    SetProgress(completedProgress, totalProgress);
                    continue;
                }

                for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
                {
                    if (token.IsCancellationRequested) break;

                    var sourceFile = files[fileIndex];
                    var destinationFile = Path.Combine(destination, Path.GetFileName(sourceFile));
                    if (simulate)
                    {
                        Log($"    [SIMULACAO] {Path.GetFileName(sourceFile)}");
                        copiedFiles++;
                        SetProgress(completedProgress + ScaleFileProgress(fileIndex + 1, files.Count, progressScale), totalProgress);
                        continue;
                    }

                    if (TryTransferFile(sourceFile, destinationFile, config, out var error))
                    {
                        copiedFiles++;
                        Log($"    [OK] {Path.GetFileName(sourceFile)}");
                    }
                    else
                    {
                        errors++;
                        Log($"    [ERRO] {Path.GetFileName(sourceFile)} - {error}");
                    }

                    SetProgress(completedProgress + ScaleFileProgress(fileIndex + 1, files.Count, progressScale), totalProgress);
                }

                completedProgress += progressScale;
                SetProgress(completedProgress, totalProgress);
            }
        }

        if (!token.IsCancellationRequested)
        {
            SetProgress(totalProgress, totalProgress);
        }

        Log("");
        Log("==========================================================");
        Log(token.IsCancellationRequested ? "Cancelado pelo usuario" : "Resumo final");
        Log("==========================================================");
        Log($"Arquivos encontrados : {foundFiles}");
        Log($"Arquivos processados : {copiedFiles}");
        Log($"Avisos               : {warnings}");
        Log($"Erros                : {errors}");
        Log($"Log                  : {_currentLogFile}");

        if (foundFiles == 0)
        {
            Log("[AVISO] Nenhum XML foi encontrado. Confira mes, ano, CNPJ, UF e origem.");
        }
    }

    private static int ScaleFileProgress(int processedFiles, int totalFiles, int progressScale)
    {
        if (totalFiles <= 0)
        {
            return progressScale;
        }

        return Math.Min(progressScale, (int)Math.Round(processedFiles * progressScale / (double)totalFiles));
    }

    private static bool ValidateCompany(CompanyConfig company, out string message)
    {
        if (string.IsNullOrWhiteSpace(company.Name))
        {
            message = "Nome da empresa nao informado.";
            return false;
        }

        if (!IsDigits(company.Cnpj, 14))
        {
            message = $"CNPJ invalido: {company.Cnpj}";
            return false;
        }

        if (!IsDigits(company.Uf, 2))
        {
            message = $"Estado invalido: {company.Uf}. Selecione um estado na lista.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(company.OriginPath))
        {
            message = "Pasta de origem nao informada.";
            return false;
        }

        message = "";
        return true;
    }

    private static bool IsDigits(string value, int length)
    {
        return value.Length == length && value.All(char.IsDigit);
    }

    private static List<StateOption> StateOptions() => new()
    {
        new() { Code = "11", Name = "Rondonia" },
        new() { Code = "12", Name = "Acre" },
        new() { Code = "13", Name = "Amazonas" },
        new() { Code = "14", Name = "Roraima" },
        new() { Code = "15", Name = "Para" },
        new() { Code = "16", Name = "Amapa" },
        new() { Code = "17", Name = "Tocantins" },
        new() { Code = "21", Name = "Maranhao" },
        new() { Code = "22", Name = "Piaui" },
        new() { Code = "23", Name = "Ceara" },
        new() { Code = "24", Name = "Rio Grande do Norte" },
        new() { Code = "25", Name = "Paraiba" },
        new() { Code = "26", Name = "Pernambuco" },
        new() { Code = "27", Name = "Alagoas" },
        new() { Code = "28", Name = "Sergipe" },
        new() { Code = "29", Name = "Bahia" },
        new() { Code = "31", Name = "Minas Gerais" },
        new() { Code = "32", Name = "Espirito Santo" },
        new() { Code = "33", Name = "Rio de Janeiro" },
        new() { Code = "35", Name = "Sao Paulo" },
        new() { Code = "41", Name = "Parana" },
        new() { Code = "42", Name = "Santa Catarina" },
        new() { Code = "43", Name = "Rio Grande do Sul" },
        new() { Code = "50", Name = "Mato Grosso do Sul" },
        new() { Code = "51", Name = "Mato Grosso" },
        new() { Code = "52", Name = "Goias" },
        new() { Code = "53", Name = "Distrito Federal" }
    };

    private static List<(string DocType, string Status, string Model, string Suffix)> FileGroups() => new()
    {
        ("NFe", "Autorizadas", "55", "-nfe.xml"),
        ("NFe", "Canceladas", "55", "-can.xml"),
        ("NFCe", "Autorizadas", "65", "-nfe.xml"),
        ("NFCe", "Canceladas", "65", "-can.xml")
    };

    private List<string> FindFiles(string origin, string mask, ref int errors)
    {
        try
        {
            return Directory.EnumerateFiles(origin, mask, SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            errors++;
            Log($"  [ERRO] Falha ao listar arquivos em {origin}");
            Log($"         {ex.Message}");
            return new List<string>();
        }
    }

    private bool TryTransferFile(string sourceFile, string destinationFile, XmlCopyConfig config, out string error)
    {
        var attempts = config.RetryCount + 1;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);

                if (string.Equals(config.Action, "COPY", StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourceFile, destinationFile, overwrite: true);
                }
                else
                {
                    if (File.Exists(destinationFile))
                    {
                        File.Delete(destinationFile);
                    }

                    File.Move(sourceFile, destinationFile);
                }

                error = "";
                return true;
            }
            catch (Exception ex) when (attempt < attempts)
            {
                Log($"      Tentativa {attempt} falhou: {ex.Message}");
                Thread.Sleep(TimeSpan.FromSeconds(config.RetryDelaySeconds));
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        error = "Falha desconhecida.";
        return false;
    }

    private void SetProgress(int value, int max)
    {
        void Apply()
        {
            var normalizedMax = Math.Max(1, max);
            var normalizedValue = Math.Min(Math.Max(0, value), normalizedMax);

            _progress.Maximum = normalizedMax;
            _progress.Value = normalizedValue;
            _taskbarProgress.SetState(Handle, TaskbarProgressState.Normal);
            _taskbarProgress.SetProgress(Handle, normalizedValue, normalizedMax);
            SetWindowProgressIcon(normalizedValue, normalizedMax);
        }

        if (InvokeRequired) BeginInvoke(Apply); else Apply();
    }

    private void SetStatus(string status)
    {
        void Apply() => _lblStatus.Text = status;
        if (InvokeRequired) BeginInvoke(Apply); else Apply();
    }

    private void ScheduleProgressClear()
    {
        var version = ++_progressClearVersion;

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5));

            if (IsDisposed)
            {
                return;
            }

            BeginInvoke(new Action(() =>
            {
                if (_cts is null && version == _progressClearVersion)
                {
                    _taskbarProgress.SetState(Handle, TaskbarProgressState.NoProgress);
                    RestoreBaseIcon();
                }
            }));
        });
    }

    private void SetWindowProgressIcon(int value, int max)
    {
        if (_appIcon is null || max <= 0)
        {
            return;
        }

        var percent = Math.Clamp((int)Math.Round(value * 100.0 / max), 0, 100);
        if (percent == _lastIconProgressPercent)
        {
            return;
        }

        _lastIconProgressPercent = percent;
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using (var baseIcon = _appIcon.ToBitmap())
        {
            graphics.DrawImage(baseIcon, new Rectangle(0, 0, 32, 32));
        }

        var barBounds = new Rectangle(3, 25, 26, 5);
        using var barBackground = new SolidBrush(Color.FromArgb(230, AppTheme.PrimaryLight));
        using var barFill = new SolidBrush(AppTheme.Primary);
        using var barBorder = new Pen(AppTheme.PrimaryDark);

        graphics.FillRectangle(barBackground, barBounds);
        var fillWidth = Math.Max(1, (int)Math.Round(barBounds.Width * (percent / 100.0)));
        graphics.FillRectangle(barFill, new Rectangle(barBounds.X, barBounds.Y, fillWidth, barBounds.Height));
        graphics.DrawRectangle(barBorder, barBounds);

        var handle = bitmap.GetHicon();
        try
        {
            var newIcon = (Icon)Icon.FromHandle(handle).Clone();
            var previousIcon = _progressIcon;
            _progressIcon = newIcon;
            Icon = _progressIcon;
            previousIcon?.Dispose();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    private void RestoreBaseIcon()
    {
        _lastIconProgressPercent = -1;
        if (_appIcon is not null)
        {
            Icon = _appIcon;
        }

        _progressIcon?.Dispose();
        _progressIcon = null;
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";

        lock (_logLock)
        {
            if (!string.IsNullOrWhiteSpace(_currentLogFile))
            {
                File.AppendAllText(_currentLogFile, line + Environment.NewLine);
            }
        }

        void Append()
        {
            _txtLog.AppendText(line + Environment.NewLine);
            _txtLog.SelectionStart = _txtLog.TextLength;
            _txtLog.ScrollToCaret();
        }

        if (InvokeRequired) BeginInvoke(Append); else Append();
    }
}

internal sealed class DatabaseConnectionDialog : Form
{
    private readonly CheckBox _chkEnabled = new();
    private readonly TextBox _txtServer = new();
    private readonly NumericUpDown _numPort = new();
    private readonly TextBox _txtDatabase = new();
    private readonly TextBox _txtUsername = new();
    private readonly TextBox _txtPassword = new();
    private readonly TextBox _txtDestinationPath = new();
    private readonly Button _btnTest = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnCancel = new();
    private readonly Label _lblStatus = new();
    private bool _connectionTested;

    public DatabaseConnectionConfig? Connection { get; private set; }

    public DatabaseConnectionDialog(DatabaseConnectionConfig? connection = null)
    {
        Text = "Conexão PostgreSQL";
        Width = 560;
        Height = 390;
        MinimumSize = new Size(520, 360);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = AppTheme.WindowBackground;
        ForeColor = AppTheme.Text;
        Font = new Font("Segoe UI", 9F);

        BuildUi();
        Bind(connection ?? new DatabaseConnectionConfig());
        ResetConnectionTest();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        Controls.Add(root);

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < fields.RowCount; i++)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        }

        _chkEnabled.Text = "Ativa";
        _chkEnabled.Dock = DockStyle.Fill;

        _txtServer.Dock = DockStyle.Fill;
        _numPort.Minimum = 1;
        _numPort.Maximum = 65535;
        _numPort.Value = 5432;
        _numPort.Dock = DockStyle.Left;
        _numPort.Width = 110;
        _txtDatabase.Dock = DockStyle.Fill;
        _txtUsername.Dock = DockStyle.Fill;
        _txtPassword.Dock = DockStyle.Fill;
        _txtPassword.UseSystemPasswordChar = true;
        _txtDestinationPath.Dock = DockStyle.Fill;
        _txtDestinationPath.PlaceholderText = "/sist/icomp/empresa/exp";

        fields.Controls.Add(new Label { Text = "Ativa", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
        fields.Controls.Add(_chkEnabled, 1, 0);
        AddTextField(fields, "Servidor", _txtServer, 1);
        fields.Controls.Add(new Label { Text = "Porta", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 2);
        fields.Controls.Add(_numPort, 1, 2);
        AddTextField(fields, "Banco de dados", _txtDatabase, 3);
        AddTextField(fields, "Usuário", _txtUsername, 4);
        AddTextField(fields, "Senha", _txtPassword, 5);
        AddTextField(fields, "Caminho de destino", _txtDestinationPath, 6);
        root.Controls.Add(fields, 0, 0);

        _lblStatus.Text = "Teste pendente.";
        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.ForeColor = AppTheme.TextMuted;
        root.Controls.Add(_lblStatus, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        _btnCancel.Text = "Cancelar";
        _btnSave.Text = "Salvar configuração";
        _btnTest.Text = "Testar conexão";
        _btnSave.Enabled = false;

        foreach (var button in new[] { _btnCancel, _btnSave, _btnTest })
        {
            button.Width = button == _btnSave ? 150 : 130;
            button.Height = 30;
            StyleButton(button, primary: button == _btnSave);
            buttons.Controls.Add(button);
        }

        _btnCancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        _btnTest.Click += async (_, _) => await TestConnectionAsync();
        _btnSave.Click += (_, _) => SaveConnection();
        root.Controls.Add(buttons, 0, 2);

        foreach (var textBox in new[] { _txtServer, _txtDatabase, _txtUsername, _txtPassword, _txtDestinationPath })
        {
            textBox.TextChanged += (_, _) => ResetConnectionTest();
            StyleTextBox(textBox);
        }

        _numPort.ValueChanged += (_, _) => ResetConnectionTest();
        _chkEnabled.CheckedChanged += (_, _) => ResetConnectionTest();
        _numPort.BackColor = AppTheme.InputBackground;
        _numPort.ForeColor = AppTheme.Text;
        _chkEnabled.BackColor = AppTheme.WindowBackground;
        _chkEnabled.ForeColor = AppTheme.Text;
    }

    private static void AddTextField(TableLayoutPanel fields, string label, TextBox textBox, int row)
    {
        fields.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        fields.Controls.Add(textBox, 1, row);
    }

    private void Bind(DatabaseConnectionConfig connection)
    {
        _chkEnabled.Checked = connection.Enabled;
        _txtServer.Text = connection.Server;
        _numPort.Value = Math.Clamp(connection.Port <= 0 ? 5432 : connection.Port, 1, 65535);
        _txtDatabase.Text = connection.Database;
        _txtUsername.Text = connection.Username;
        _txtPassword.Text = connection.Password;
        _txtDestinationPath.Text = connection.DestinationPath;
    }

    private async Task TestConnectionAsync()
    {
        if (!TryBuildConnectionConfig(requireDestinationPath: false, out var config, out var message))
        {
            MessageBox.Show(message, "Conexão PostgreSQL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetTestingState(testing: true);
        try
        {
            await using var connection = new NpgsqlConnection(BuildConnectionString(config));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await connection.OpenAsync(cts.Token);
            _connectionTested = true;
            _btnSave.Enabled = true;
            _lblStatus.Text = "Conexão testada com sucesso.";
            _lblStatus.ForeColor = AppTheme.PrimaryDark;
        }
        catch (PostgresException ex)
        {
            ResetConnectionTest("Falha no teste de conexão.");
            MessageBox.Show($"Não consegui conectar ao PostgreSQL.\n\n{FormatPostgresError(ex, config)}", "Conexão PostgreSQL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException)
        {
            ResetConnectionTest("Tempo limite ao testar a conexão.");
            MessageBox.Show("Tempo limite ao testar a conexão.", "Conexão PostgreSQL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            ResetConnectionTest("Falha no teste de conexão.");
            MessageBox.Show($"Não consegui conectar ao PostgreSQL.\n\n{NormalizeDatabaseMessage(ex.Message)}", "Conexão PostgreSQL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetTestingState(testing: false);
        }
    }

    private void SaveConnection()
    {
        if (!_connectionTested)
        {
            return;
        }

        if (!TryBuildConnectionConfig(requireDestinationPath: true, out var config, out var message))
        {
            MessageBox.Show(message, "Conexão PostgreSQL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Connection = config;
        DialogResult = DialogResult.OK;
    }

    private bool TryBuildConnectionConfig(bool requireDestinationPath, out DatabaseConnectionConfig config, out string message)
    {
        config = new DatabaseConnectionConfig
        {
            Enabled = _chkEnabled.Checked,
            Server = _txtServer.Text.Trim(),
            Port = (int)_numPort.Value,
            Database = _txtDatabase.Text.Trim(),
            Username = _txtUsername.Text.Trim(),
            Password = _txtPassword.Text,
            DestinationPath = _txtDestinationPath.Text.Trim()
        };

        if (string.IsNullOrWhiteSpace(config.Server))
        {
            message = "Informe o servidor.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(config.Database))
        {
            message = "Informe o banco de dados.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(config.Username))
        {
            message = "Informe o usuário.";
            return false;
        }

        if (requireDestinationPath && string.IsNullOrWhiteSpace(config.DestinationPath))
        {
            message = "Informe o caminho de destino. Exemplo: /sist/icomp/empresa/exp";
            return false;
        }

        if (requireDestinationPath && !config.DestinationPath.StartsWith("/", StringComparison.Ordinal))
        {
            message = "O caminho de destino deve estar no formato Linux. Exemplo: /sist/icomp/empresa/exp";
            return false;
        }

        message = "";
        return true;
    }

    internal static string FormatPostgresError(PostgresException ex, DatabaseConnectionConfig config)
    {
        if (string.Equals(ex.SqlState, "3D000", StringComparison.OrdinalIgnoreCase))
        {
            return $"{ex.SqlState}: banco de dados \"{config.Database}\" não existe";
        }

        if (string.Equals(ex.SqlState, "28P01", StringComparison.OrdinalIgnoreCase))
        {
            return $"{ex.SqlState}: usuário ou senha inválidos";
        }

        var message = NormalizeDatabaseMessage(ex.MessageText);
        return string.IsNullOrWhiteSpace(ex.SqlState) ? message : $"{ex.SqlState}: {message}";
    }

    internal static string NormalizeDatabaseMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        var normalized = message
            .Replace("n�o", "não", StringComparison.OrdinalIgnoreCase)
            .Replace("usu�rio", "usuário", StringComparison.OrdinalIgnoreCase)
            .Replace("conex�o", "conexão", StringComparison.OrdinalIgnoreCase)
            .Replace("rela��o", "relação", StringComparison.OrdinalIgnoreCase)
            .Replace("fun��o", "função", StringComparison.OrdinalIgnoreCase)
            .Replace("configura��o", "configuração", StringComparison.OrdinalIgnoreCase)
            .Replace("inv�lido", "inválido", StringComparison.OrdinalIgnoreCase)
            .Replace("inv�lida", "inválida", StringComparison.OrdinalIgnoreCase)
            .Replace("autentica��o", "autenticação", StringComparison.OrdinalIgnoreCase);

        return normalized
            .Replace("N�o", "Não", StringComparison.Ordinal)
            .Replace("Ã£", "ã", StringComparison.Ordinal)
            .Replace("Ã¡", "á", StringComparison.Ordinal)
            .Replace("Ã¢", "â", StringComparison.Ordinal)
            .Replace("Ã©", "é", StringComparison.Ordinal)
            .Replace("Ãª", "ê", StringComparison.Ordinal)
            .Replace("Ã­", "í", StringComparison.Ordinal)
            .Replace("Ã³", "ó", StringComparison.Ordinal)
            .Replace("Ã´", "ô", StringComparison.Ordinal)
            .Replace("Ãº", "ú", StringComparison.Ordinal)
            .Replace("Ã§", "ç", StringComparison.Ordinal)
            .Replace("Ã‡", "Ç", StringComparison.Ordinal);
    }

    internal static string BuildConnectionString(DatabaseConnectionConfig config)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = config.Server,
            Port = config.Port,
            Database = config.Database,
            Username = config.Username,
            Password = config.Password,
            Pooling = false,
            Timeout = 8,
            CommandTimeout = 8,
            SslMode = SslMode.Disable
        };

        return builder.ConnectionString;
    }

    private void SetTestingState(bool testing)
    {
        _btnTest.Enabled = !testing;
        _btnCancel.Enabled = !testing;
        _btnSave.Enabled = !testing && _connectionTested;
        _lblStatus.Text = testing ? "Testando conexão..." : _lblStatus.Text;
        UseWaitCursor = testing;
    }

    private void ResetConnectionTest(string status = "Teste pendente.")
    {
        _connectionTested = false;
        _btnSave.Enabled = false;
        _lblStatus.Text = status;
        _lblStatus.ForeColor = AppTheme.TextMuted;
    }

    private static void StyleButton(Button button, bool primary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = primary ? AppTheme.PrimaryDark : AppTheme.PrimaryLight;
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(49, 27, 146) : Color.FromArgb(211, 198, 232);

        if (primary)
        {
            button.BackColor = AppTheme.Primary;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderColor = AppTheme.PrimaryDark;
        }
        else
        {
            button.BackColor = AppTheme.SurfaceAlt;
            button.ForeColor = AppTheme.PrimaryDark;
            button.FlatAppearance.BorderColor = AppTheme.Border;
        }
    }

    private static void StyleTextBox(TextBox textBox)
    {
        textBox.BackColor = AppTheme.InputBackground;
        textBox.ForeColor = AppTheme.Text;
        textBox.BorderStyle = BorderStyle.FixedSingle;
    }
}

internal sealed class PurpleProgressBar : Control
{
    private int _maximum = 1;
    private int _value;

    public PurpleProgressBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Height = 18;
        BackColor = Color.FromArgb(238, 232, 246);
        ForeColor = Color.FromArgb(126, 87, 194);
    }

    [DefaultValue(1)]
    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(1, value);
            _value = Math.Min(_value, _maximum);
            Invalidate();
        }
    }

    [DefaultValue(0)]
    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, _maximum);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var bounds = ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var background = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(background, bounds);

        var progressWidth = (int)Math.Round(bounds.Width * (_value / (double)_maximum));
        if (progressWidth > 0)
        {
            using var foreground = new SolidBrush(ForeColor);
            e.Graphics.FillRectangle(foreground, new Rectangle(bounds.X, bounds.Y, progressWidth, bounds.Height));
        }

        using var border = new Pen(Color.FromArgb(196, 181, 218));
        e.Graphics.DrawRectangle(border, new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1));
    }
}

internal enum TaskbarProgressState
{
    NoProgress = 0,
    Indeterminate = 1,
    Normal = 2,
    Error = 4,
    Paused = 8
}

internal static partial class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}

internal sealed class TaskbarProgress
{
    private ITaskbarList3? _taskbar;
    private bool _available = true;

    public void SetProgress(IntPtr handle, int value, int max)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var instance = GetInstance();
        instance?.SetProgressValue(handle, (ulong)Math.Max(0, value), (ulong)Math.Max(1, max));
    }

    public void SetState(IntPtr handle, TaskbarProgressState state)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        GetInstance()?.SetProgressState(handle, state);
    }

    private ITaskbarList3? GetInstance()
    {
        if (!_available)
        {
            return null;
        }

        if (_taskbar is not null)
        {
            return _taskbar;
        }

        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            {
                _available = false;
                return null;
            }

            _taskbar = (ITaskbarList3)new CTaskbarList();
            _taskbar.HrInit();
            return _taskbar;
        }
        catch
        {
            _available = false;
            return null;
        }
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class CTaskbarList
    {
    }

    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EE00C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        [PreserveSig]
        int HrInit();

        [PreserveSig]
        int AddTab(IntPtr hwnd);

        [PreserveSig]
        int DeleteTab(IntPtr hwnd);

        [PreserveSig]
        int ActivateTab(IntPtr hwnd);

        [PreserveSig]
        int SetActiveAlt(IntPtr hwnd);

        [PreserveSig]
        int MarkFullscreenWindow(IntPtr hwnd, bool fFullscreen);

        [PreserveSig]
        int SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);

        [PreserveSig]
        int SetProgressState(IntPtr hwnd, TaskbarProgressState tbpFlags);

        [PreserveSig]
        int RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);

        [PreserveSig]
        int UnregisterTab(IntPtr hwndTab);

        [PreserveSig]
        int SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);

        [PreserveSig]
        int SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, uint dwReserved);

        [PreserveSig]
        int ThumbBarAddButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);

        [PreserveSig]
        int ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);

        [PreserveSig]
        int ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);

        [PreserveSig]
        int SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, string? pszDescription);

        [PreserveSig]
        int SetThumbnailTooltip(IntPtr hwnd, string? pszTip);

        [PreserveSig]
        int SetThumbnailClip(IntPtr hwnd, IntPtr prcClip);
    }
}
