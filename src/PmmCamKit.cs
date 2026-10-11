// PmmCamKit：MMD 工程（PMM）相机工具 —— 合并相机骨 / 相机追踪批量绑定
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyTitle("PmmCamKit")]
[assembly: AssemblyProduct("PmmCamKit")]
[assembly: AssemblyDescription("MMD PMM camera tools: merge camera rigs, batch camera bone follow")]
[assembly: AssemblyCopyright("CicloO")]
[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyFileVersion("0.2.0.0")]

namespace PmmCamKit
{
    class ModelItem
    {
        public int Id; public string Text;
        public override string ToString() { return Text; }
    }

    class MainForm : Form
    {
        public const string Version = "0.2";

        // 切换语言时重新执行的设置文字动作
        readonly List<Action> texts = new List<Action>();
        void Tr(Action a) { texts.Add(a); a(); }
        T Tx<T>(T c, string zh, string ja, string en) where T : Control { Tr(() => c.Text = L.T(zh, ja, en)); return c; }

        // ---- 合并页 ----
        readonly TextBox tbRender = new TextBox(), tbRig = new TextBox(), tbOut = new TextBox();
        readonly CheckedListBox lbModels = new CheckedListBox();
        readonly CheckBox cbCamera = new CheckBox();
        readonly DataGridView grid = new DataGridView();
        readonly TextBox mergeLog = new TextBox();
        PmmFile render, rig;
        List<ExternalRef> refs = new List<ExternalRef>();
        readonly Dictionary<int, string> pathOverride = new Dictionary<int, string>();
        bool loading;
        const int ColTargetModel = 6, ColTargetBone = 7;

        // ---- 追踪页 ----
        readonly RadioButton rbForward = new RadioButton(), rbAll = new RadioButton();
        readonly TextBox followLog = new TextBox();

        public MainForm(string[] initial)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size(1000, 800);
            MinimumSize = new Size(760, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Tr(() => Text = "PmmCamKit " + Version + " — " + L.T("MMD 工程相机工具", "MMD プロジェクト用カメラツール", "MMD project camera tools"));

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var tMerge = Tx(new TabPage(), "合并相机骨", "カメラボーンを統合", "Merge camera rig");
            var tFollow = Tx(new TabPage(), "追踪批量绑定", "追従の一括設定", "Batch camera follow");
            BuildMergePage(tMerge);
            BuildFollowPage(tFollow);
            tabs.TabPages.Add(tMerge);
            tabs.TabPages.Add(tFollow);

            // 右上角语言切换
            // 放在选项卡标签那一行的右侧空白处（悬浮在 TabControl 之上，不单独占一行）
            var langBar = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0), Margin = new Padding(0) };
            var cbLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            cbLang.Items.AddRange(L.Names);
            cbLang.SelectedIndex = L.Cur;
            cbLang.SelectedIndexChanged += (s, e) => { L.Cur = cbLang.SelectedIndex; L.Save(); ApplyLanguage(); };
            langBar.Controls.Add(cbLang);
            langBar.Controls.Add(new Label { Text = "Language / 语言 / 言語", AutoSize = true, Margin = new Padding(3, 4, 3, 0) });

            Controls.Add(tabs);
            Controls.Add(langBar);
            langBar.BringToFront();
            Action placeLang = () => langBar.Location = new Point(ClientSize.Width - langBar.Width - 6, Math.Max(0, (tabs.ItemSize.Height + 2 - langBar.Height) / 2));
            Layout += (s, e) => placeLang();
            langBar.SizeChanged += (s, e) => placeLang();
            ResumeLayout(false);
            PerformLayout();

            // 启动参数：--render 目标.pmm --rig 相机骨.pmm 预填合并页；其余 .pmm 参数交给追踪页处理
            int ri = Array.IndexOf(initial, "--render"), gi = Array.IndexOf(initial, "--rig");
            Shown += (s, e) =>
            {
                if (ri >= 0 && ri + 1 < initial.Length && gi >= 0 && gi + 1 < initial.Length)
                {
                    tbRender.Text = initial[ri + 1]; tbRig.Text = initial[gi + 1]; LoadFiles();
                }
                else if (initial.Length > 0) { tabs.SelectedTab = tFollow; RunFollow(initial); }
            };
        }

        void ApplyLanguage()
        {
            foreach (var a in texts) a();
            if (render != null && rig != null)
            {
                var names = new HashSet<string>(render.Models.Select(m => m.Name));
                loading = true;
                for (int i = 0; i < lbModels.Items.Count; i++)
                {
                    var it = (ModelItem)lbModels.Items[i];
                    it.Text = ItemText(rig.ById(it.Id), names.Contains(rig.ById(it.Id).Name));
                    lbModels.Items[i] = it;
                }
                loading = false;
                RefreshRefs();
            }
        }

        static TextBox MakeLog(TextBox t)
        {
            t.Multiline = true; t.ReadOnly = true; t.ScrollBars = ScrollBars.Vertical;
            t.Dock = DockStyle.Fill; t.BackColor = SystemColors.Window;
            return t;
        }

        // 自动换行的说明文字：宽度跟随父容器
        static Label WrapLabel(Control parent)
        {
            var l = new Label { AutoSize = true, Margin = new Padding(3, 3, 3, 6) };
            parent.SizeChanged += (s, e) => l.MaximumSize = new Size(Math.Max(100, parent.ClientSize.Width - 24), 0);
            return l;
        }

        static Label HintLabel()
        {
            return new Label { AutoSize = true, Dock = DockStyle.Top, ForeColor = SystemColors.GrayText, Padding = new Padding(0, 0, 0, 4) };
        }

        // 统一样式的按钮：自动宽度，靠右排列
        static Button MakeButton(bool primary = false)
        {
            var b = new Button { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 3, 10, 3), MinimumSize = new Size(primary ? 140 : 96, 0), Margin = new Padding(6, 4, 0, 4) };
            return b;
        }

        static FlowLayoutPanel ButtonRow(params Control[] cs)
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0) };
            foreach (var c in cs) p.Controls.Add(c);
            return p;
        }

        // ===================== 合并相机骨 =====================
        void BuildMergePage(TabPage page)
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 说明
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 文件
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));    // 模型列表
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 相机替换
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));    // 绑定表
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 合并按钮
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 25));    // 日志

            root.Controls.Add(Tx(WrapLabel(root),
                "把「相机骨 PMM」里的相机骨模型（含骨骼关键帧）和相机移入「目标 PMM」。移入的模型接在模型列表末尾，目标 PMM 原有内容不变，同名 EMM 一起生成；原文件不会被修改。",
                "「カメラボーン PMM」のカメラボーンモデル（ボーンのキーフレームを含む）とカメラを「統合先 PMM」へ移します。モデルはモデル一覧の末尾に追加され、統合先 PMM の既存内容は変わりません。同名の EMM も作成します。元のファイルは変更しません。",
                "Moves the camera-rig model (with its bone keyframes) and the camera from the \"camera-rig PMM\" into the \"target PMM\". Moved models are appended to the end of the model list, the target PMM's existing content stays unchanged, and a matching EMM is written too. The original files are never modified."));

            var files = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, Margin = new Padding(0) };
            files.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            files.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            files.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            AddFileRow(files, tbRender, true, "目标 PMM：", "統合先 PMM：", "Target PMM:");
            AddFileRow(files, tbRig, true, "相机骨 PMM：", "カメラボーン PMM：", "Camera-rig PMM:");
            AddFileRow(files, tbOut, false, "输出：", "出力：", "Output:");
            root.Controls.Add(files);

            // 模型列表
            var g1 = Tx(new GroupBox { Dock = DockStyle.Fill, Padding = new Padding(6, 4, 6, 6) }, "要移入的模型", "移すモデル", "Models to move");
            var g1t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            g1t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            g1t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            g1t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            g1t.Controls.Add(Tx(HintLabel(),
                "默认勾选像相机骨的模型（名称或骨骼名带「カメラ / camera / 镜头」等，且骨骼较少）。找不到的模型文件会自动按文件名搜索。",
                "カメラボーンらしいモデル（名前やボーン名に「カメラ / camera」などを含み、ボーン数が少ないもの）を既定でチェックします。見つからないモデルファイルはファイル名で自動検索します。",
                "Models that look like camera rigs (\"camera\", \"カメラ\", ... in the name or bone names, few bones) are checked by default. Missing model files are searched for by file name."));
            lbModels.Dock = DockStyle.Fill; lbModels.CheckOnClick = true; lbModels.HorizontalScrollbar = true; lbModels.IntegralHeight = false; lbModels.MinimumSize = new Size(0, 64);
            // 只有点在勾选框上才切换勾选；点文字只选中该行（方便「指定所选模型的文件…」）
            bool textClick = false;
            lbModels.MouseDown += (s, e) =>
            {
                int i = lbModels.IndexFromPoint(e.Location);
                float dpi; using (var g = lbModels.CreateGraphics()) dpi = g.DpiX / 96f;
                textClick = i >= 0 && e.X - lbModels.GetItemRectangle(i).X > (int)(18 * dpi);
            };
            lbModels.MouseUp += (s, e) => BeginInvoke(new Action(() => textClick = false));
            lbModels.ItemCheck += (s, e) =>
            {
                if (textClick && !loading) { e.NewValue = e.CurrentValue; return; }
                BeginInvoke(new Action(RefreshRefs));
            };
            g1t.Controls.Add(lbModels);
            g1.Controls.Add(g1t);
            root.Controls.Add(g1);

            Tx(cbCamera, "用相机骨 PMM 的相机替换目标 PMM 的相机", "統合先 PMM のカメラをカメラボーン PMM のカメラに置き換える", "Replace the target PMM's camera with the camera-rig PMM's camera");
            cbCamera.AutoSize = true; cbCamera.Checked = true; cbCamera.Margin = new Padding(3, 6, 3, 6);
            cbCamera.CheckedChanged += (s, e) => RefreshRefs();
            cbCamera.Anchor = AnchorStyles.Left;
            // 同一行：左边相机替换选项，右边「指定模型文件」按钮（与下方「合并」按钮右对齐）
            var pick = Tx(MakeButton(), "指定所选模型的文件…", "選択したモデルのファイルを指定…", "Locate selected model file…");
            pick.Click += (s, e) => PickModelFile();
            var mid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0) };
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            mid.Controls.Add(cbCamera);
            mid.Controls.Add(pick);
            root.Controls.Add(mid);

            // 绑定替换表
            var g2 = Tx(new GroupBox { Dock = DockStyle.Fill, Padding = new Padding(6, 4, 6, 6) }, "绑定替换", "割り当ての置き換え", "Binding targets");
            var g2t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            g2t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            g2t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            g2t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            g2t.Controls.Add(Tx(HintLabel(),
                "相机骨模型的外部亲、相机追踪所引用的其他模型/骨骼，在目标 PMM 中对应到哪里。关键帧中途换了目标时会分成多行，可分别指定。",
                "カメラボーンモデルの外部親やカメラ追従が参照している他のモデル／ボーンを、統合先 PMM のどれに割り当てるか。途中で参照先が変わる場合は行が分かれ、個別に指定できます。",
                "Where the other models/bones referenced by the rig's outside parents and the camera follow should point in the target PMM. If the target changes mid-way, each target gets its own row."));
            SetupGrid();
            grid.MinimumSize = new Size(0, 90);
            g2t.Controls.Add(grid);
            g2.Controls.Add(g2t);
            root.Controls.Add(g2);

            var btn = Tx(MakeButton(true), "合并", "統合", "Merge");
            btn.Click += (s, e) => RunMerge();
            root.Controls.Add(ButtonRow(btn));

            root.Controls.Add(MakeLog(mergeLog));
            page.Controls.Add(root);
        }

        void SetupGrid()
        {
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.BackgroundColor = SystemColors.Window;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.DefaultCellStyle.Padding = new Padding(2, 3, 2, 3);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(204, 228, 247);
            grid.DefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;

            float s;
            using (var g = CreateGraphics()) s = g.DpiX / 96f;
            Func<string, string, string, int, int, bool, DataGridViewColumn> col = (zh, ja, en, weight, minW, ro) =>
            {
                DataGridViewColumn c = ro ? (DataGridViewColumn)new DataGridViewTextBoxColumn() : new DataGridViewComboBoxColumn { FlatStyle = FlatStyle.Flat };
                c.ReadOnly = ro; c.FillWeight = weight; c.MinimumWidth = (int)(minW * s);
                c.SortMode = DataGridViewColumnSortMode.NotSortable;
                if (ro) c.DefaultCellStyle.BackColor = Color.FromArgb(247, 247, 247);
                Tr(() => c.HeaderText = L.T(zh, ja, en));
                grid.Columns.Add(c);
                return c;
            };
            col("原模型", "元モデル", "Source model", 14, 80, true);
            col("原骨骼", "元ボーン", "Source bone", 10, 64, true);
            col("被谁引用", "参照元", "Used by", 18, 100, true);
            col("帧范围", "フレーム範囲", "Frames", 10, 70, true);
            col("关键帧数", "キー数", "Keys", 7, 72, true).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            col("匹配", "一致", "Match", 14, 80, true);
            col("目标模型", "割り当て先モデル", "Target model", 18, 110, false);
            col("目标骨骼", "割り当て先ボーン", "Target bone", 12, 90, false);
            grid.CurrentCellDirtyStateChanged += (o, e) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellValueChanged += GridValueChanged;
            grid.DataError += (o, e) => { e.ThrowException = false; };
        }

        void AddFileRow(TableLayoutPanel t, TextBox tb, bool open, string zh, string ja, string en)
        {
            t.Controls.Add(Tx(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 4, 0) }, zh, ja, en));
            tb.Dock = DockStyle.Fill; tb.Margin = new Padding(3, 5, 3, 3);
            t.Controls.Add(tb);
            var b = Tx(MakeButton(), "浏览…", "参照…", "Browse…");
            b.Margin = new Padding(3, 3, 0, 3);
            b.Click += (s, e) =>
            {
                string filter = L.T("MMD 工程文件", "MMD プロジェクトファイル", "MMD project file") + " (*.pmm)|*.pmm";
                if (open)
                {
                    using (var d = new OpenFileDialog { Filter = filter })
                        if (d.ShowDialog(this) == DialogResult.OK) { tb.Text = d.FileName; LoadFiles(); }
                }
                else
                {
                    using (var d = new SaveFileDialog { Filter = filter, FileName = Path.GetFileName(tb.Text) })
                        if (d.ShowDialog(this) == DialogResult.OK) tb.Text = d.FileName;
                }
            };
            t.Controls.Add(b);
            if (open)
            {
                tb.AllowDrop = true;
                tb.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
                tb.DragDrop += (s, e) => { tb.Text = ((string[])e.Data.GetData(DataFormats.FileDrop))[0]; LoadFiles(); };
                tb.Leave += (s, e) => LoadFiles();
            }
        }

        string loadedRender = "", loadedRig = "";

        void LoadFiles()
        {
            if (tbRender.Text == loadedRender && tbRig.Text == loadedRig) return;
            loadedRender = tbRender.Text; loadedRig = tbRig.Text;
            render = rig = null;
            lbModels.Items.Clear(); grid.Rows.Clear(); refs.Clear();
            try
            {
                if (File.Exists(tbRender.Text))
                {
                    render = PmmFile.Load(tbRender.Text);
                    tbOut.Text = Merger.DefaultOutput(tbRender.Text);
                    mergeLog.AppendText(L.T("目标 PMM：模型 ", "統合先 PMM：モデル ", "Target PMM: ") + render.Models.Count
                        + L.T(" 个，相机关键帧 ", " 個、カメラキーフレーム ", " models, ") + render.CameraKeys.Count + L.T(" 个", " 個", " camera keyframes") + "\r\n");
                }
                if (File.Exists(tbRig.Text))
                {
                    rig = PmmFile.Load(tbRig.Text);
                    mergeLog.AppendText(L.T("相机骨 PMM：模型 ", "カメラボーン PMM：モデル ", "Camera-rig PMM: ") + rig.Models.Count
                        + L.T(" 个，相机关键帧 ", " 個、カメラキーフレーム ", " models, ") + rig.CameraKeys.Count + L.T(" 个", " 個", " camera keyframes") + "\r\n");
                }
            }
            catch (Exception ex) { mergeLog.AppendText(L.T("读取失败：", "読み込み失敗：", "Failed to read: ") + ex.Message + "\r\n"); }
            if (render == null || rig == null) return;

            loading = true;
            pathOverride.Clear();
            var names = new HashSet<string>(render.Models.Select(m => m.Name));
            var dirs = new[] { Path.GetDirectoryName(rig.FilePath), Path.GetDirectoryName(render.FilePath) };
            var rigLike = new HashSet<int>(rig.Models.Where(m => !names.Contains(m.Name) && m.LooksLikeCameraRig()).Select(m => m.Id));
            if (rigLike.Count == 0)
                rigLike = new HashSet<int>(rig.Models.Where(m => !names.Contains(m.Name) && m.Bones.Count <= 120).Select(m => m.Id));
            foreach (var m in rig.Models)
            {
                if (!names.Contains(m.Name) && !File.Exists(m.Path))
                {
                    string hit = Merger.ResolveModelPath(m, dirs);
                    if (hit != null) pathOverride[m.Id] = hit;
                }
                lbModels.Items.Add(new ModelItem { Id = m.Id, Text = ItemText(m, names.Contains(m.Name)) }, rigLike.Contains(m.Id));
            }
            loading = false;
            RefreshRefs();
        }

        string ItemText(PmmModel m, bool dup)
        {
            string path; bool ov = pathOverride.TryGetValue(m.Id, out path);
            if (!ov) path = m.Path;
            string st = File.Exists(path)
                ? (ov ? L.T("  ✔ 已重新定位：", "  ✔ 再指定済み：", "  ✔ relocated: ") : L.T("  ✔ 文件：", "  ✔ ファイル：", "  ✔ file: ")) + path
                : L.T("  ⚠ 找不到模型文件：", "  ⚠ モデルファイルが見つかりません：", "  ⚠ model file not found: ") + path;
            return m.Name + L.T("（骨骼 ", "（ボーン ", " (") + m.Bones.Count + L.T("）", "）", " bones)")
                + (dup ? L.T("  —— 目标 PMM 里已有同名模型", "  —— 統合先 PMM に同名モデルあり", "  — already in the target PMM") : "") + st;
        }

        void PickModelFile()
        {
            if (rig == null || lbModels.SelectedIndex < 0)
            {
                mergeLog.AppendText(L.T("请先在列表里选中一个模型", "先に一覧でモデルを選択してください", "Select a model in the list first") + "\r\n"); return;
            }
            var it = (ModelItem)lbModels.SelectedItem;
            var m = rig.ById(it.Id);
            using (var d = new OpenFileDialog
            {
                Filter = L.T("MMD 模型", "MMD モデル", "MMD model") + " (*.pmx;*.pmd)|*.pmx;*.pmd",
                Title = L.T("指定「" + m.Name + "」的模型文件", "「" + m.Name + "」のモデルファイルを指定", "Locate the model file of \"" + m.Name + "\"")
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                pathOverride[m.Id] = d.FileName;
                var names = new HashSet<string>(render.Models.Select(x => x.Name));
                it.Text = ItemText(m, names.Contains(m.Name));
                loading = true;
                lbModels.Items[lbModels.SelectedIndex] = it;
                loading = false;
            }
        }

        HashSet<int> CheckedIds()
        {
            var ids = new HashSet<int>();
            foreach (var it in lbModels.CheckedItems) ids.Add(((ModelItem)it).Id);
            return ids;
        }

        static string NoneText { get { return L.T("无（解除）", "なし（解除）", "None (unbind)"); } }

        void RefreshRefs()
        {
            if (loading || render == null || rig == null) return;
            var old = refs.ToDictionary(r => ((long)r.SrcModel << 32) | (uint)r.SrcBone, r => r.Target);
            refs = Merger.CollectRefs(render, rig, CheckedIds(), cbCamera.Checked);
            foreach (var r in refs)
            {
                RefTarget t;
                if (old.TryGetValue(((long)r.SrcModel << 32) | (uint)r.SrcBone, out t)) r.Target = t;
            }
            loading = true;
            grid.Rows.Clear();
            var modelCol = (DataGridViewComboBoxColumn)grid.Columns[ColTargetModel];
            modelCol.Items.Clear();
            modelCol.Items.Add(NoneText);
            foreach (var m in render.Models) modelCol.Items.Add(Label(m));
            foreach (var r in refs)
            {
                string nl = Environment.NewLine;
                int i = grid.Rows.Add(r.SrcModelName, r.SrcBoneName,
                    string.Join(nl, r.Users.Select(u => u.Who)),
                    string.Join(nl, r.Users.Select(u => u.Range)),
                    string.Join(nl, r.Users.Select(u => u.KeyCount.ToString())),
                    r.Match, null, null);
                if (r.Kind != MatchKind.SameModel) grid.Rows[i].Cells[5].Style.ForeColor = Color.FromArgb(176, 96, 0);
                SetRowTarget(grid.Rows[i], r);
            }
            loading = false;
        }

        // 下拉框里显示「名字 [ID n]」：同名模型（如两个同名角色）也能区分
        static string Label(PmmModel m) { return m.Name + "  [ID " + m.Id + "]"; }

        void SetRowTarget(DataGridViewRow row, ExternalRef r)
        {
            var tm = r.Target.Model < 0 ? null : render.ById(r.Target.Model);
            row.Cells[ColTargetModel].Value = tm == null ? NoneText : Label(tm);
            var boneCell = (DataGridViewComboBoxCell)row.Cells[ColTargetBone];
            boneCell.Items.Clear();
            if (tm != null)
            {
                foreach (var b in tm.Bones) boneCell.Items.Add(b);
                boneCell.Value = r.Target.Bone >= 0 && r.Target.Bone < tm.Bones.Count ? tm.Bones[r.Target.Bone] : null;
            }
            else boneCell.Value = null;
        }

        void GridValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (loading || e.RowIndex < 0 || e.RowIndex >= refs.Count) return;
            var r = refs[e.RowIndex];
            var row = grid.Rows[e.RowIndex];
            loading = true;
            if (e.ColumnIndex == ColTargetModel)
            {
                string label = row.Cells[ColTargetModel].Value as string;
                int idx = render.Models.FindIndex(m => Label(m) == label);
                if (idx < 0) { r.Target.Model = -1; r.Target.Bone = 0; }
                else
                {
                    var tm = render.Models[idx];
                    r.Target.Model = tm.Id;
                    var sm = rig.ById(r.SrcModel);
                    string want = sm != null && r.SrcBone < sm.Bones.Count ? sm.Bones[r.SrcBone] : null;
                    int bi = want == null ? -1 : tm.Bones.IndexOf(want);
                    r.Target.Bone = bi >= 0 ? bi : 0;
                }
                SetRowTarget(row, r);
            }
            else if (e.ColumnIndex == ColTargetBone && r.Target.Model >= 0)
            {
                var tm = render.ById(r.Target.Model);
                int bi = tm.Bones.IndexOf(row.Cells[ColTargetBone].Value as string);
                if (bi >= 0) r.Target.Bone = bi;
            }
            loading = false;
        }

        void RunMerge()
        {
            LoadFiles();
            if (render == null || rig == null)
            {
                mergeLog.AppendText(L.T("请先选择目标 PMM 和相机骨 PMM", "先に統合先 PMM とカメラボーン PMM を選んでください", "Choose the target PMM and the camera-rig PMM first") + "\r\n"); return;
            }
            if (string.Equals(Path.GetFullPath(tbOut.Text), Path.GetFullPath(tbRender.Text), StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFullPath(tbOut.Text), Path.GetFullPath(tbRig.Text), StringComparison.OrdinalIgnoreCase))
            {
                mergeLog.AppendText(L.T("输出文件不能和输入文件相同", "出力ファイルを入力ファイルと同じにはできません", "The output file must differ from the input files") + "\r\n"); return;
            }
            string confirm = L.T("确认", "確認", "Confirm");
            var unbound = refs.Where(r => r.Target.Model < 0).ToList();
            if (unbound.Count > 0 && MessageBox.Show(this,
                    L.T("以下引用没有对应目标，合并后会解除绑定：", "次の参照は割り当て先がないため、統合後は解除されます：", "These references have no target and will be unbound:")
                    + "\r\n" + string.Join("\r\n", unbound.Select(r => rig.Describe(r.SrcModel, r.SrcBone)))
                    + "\r\n\r\n" + L.T("继续吗？", "続けますか？", "Continue?"),
                    confirm, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            if (File.Exists(tbOut.Text) && MessageBox.Show(this,
                    L.T("输出文件已存在，覆盖吗？（同名 .emm 也会一起覆盖）", "出力ファイルは既にあります。上書きしますか？（同名の .emm も上書きされます）", "The output file exists. Overwrite it? (the .emm with the same name is overwritten too)")
                    + "\r\n" + tbOut.Text, confirm, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            try
            {
                mergeLog.AppendText("\r\n==== " + L.T("合并", "統合", "Merge") + "\r\n"
                    + Norm(Merger.Merge(render, rig, CheckedIds(), cbCamera.Checked, refs, tbOut.Text, pathOverride)));
            }
            catch (Exception ex) { mergeLog.AppendText(L.T("合并失败：", "統合失敗：", "Merge failed: ") + ex.Message + "\r\n"); }
        }

        static string Norm(string s) { return s.Replace("\r\n", "\n").Replace("\n", "\r\n"); }

        // ===================== 追踪批量绑定 =====================
        void BuildFollowPage(TabPage page)
        {
            page.AllowDrop = true;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            root.Controls.Add(Tx(WrapLabel(root),
                "用法：在 MMD 里给第一个相机关键帧设置「骨骼追踪」并保存 → 在这里选择或拖入 PMM。结果另存为「原名_追踪.pmm」放在原文件旁边，原文件不会被修改。",
                "使い方：MMD で最初のカメラキーフレームに「ボーン追従」を設定して保存 → ここで PMM を選ぶかドラッグします。結果は元ファイルの隣に「元の名前_追従.pmm」として保存され、元ファイルは変更しません。",
                "Usage: in MMD, set bone follow on the first camera keyframe and save, then choose or drop the PMM here. The result is saved next to it as \"name_follow.pmm\"; the original file is never modified."));

            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            var btn = Tx(MakeButton(true), "选择 PMM…", "PMM を選択…", "Choose PMM…");
            btn.Margin = new Padding(0, 4, 6, 4);
            btn.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog { Filter = L.T("MMD 工程文件", "MMD プロジェクトファイル", "MMD project file") + " (*.pmm)|*.pmm", Multiselect = true })
                    if (dlg.ShowDialog(this) == DialogResult.OK) RunFollow(dlg.FileNames);
            };
            Tx(rbForward, "向后填充（中途可手动换目标）", "後ろへ埋める（途中で手動で追従先を変えられる）", "Fill forward (manual target changes are kept)");
            rbForward.AutoSize = true; rbForward.Checked = true; rbForward.Margin = new Padding(12, 9, 0, 0);
            Tx(rbAll, "全部覆盖", "すべて上書き", "Overwrite all");
            rbAll.AutoSize = true; rbAll.Margin = new Padding(12, 9, 0, 0);
            bar.Controls.Add(btn); bar.Controls.Add(rbForward); bar.Controls.Add(rbAll);
            root.Controls.Add(bar);

            MakeLog(followLog);
            Tr(() => { if (followLog.TextLength == 0 || followLog.Tag == null) { followLog.Text = L.T("把 PMM 文件拖到这里，或点「选择 PMM…」。", "PMM ファイルをここにドラッグするか、「PMM を選択…」を押してください。", "Drop PMM files here, or click \"Choose PMM…\".") + "\r\n"; } });
            followLog.AllowDrop = true;
            DragEventHandler enter = (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragEventHandler drop = (s, e) => RunFollow((string[])e.Data.GetData(DataFormats.FileDrop));
            page.DragEnter += enter; page.DragDrop += drop;
            followLog.DragEnter += enter; followLog.DragDrop += drop;
            root.Controls.Add(followLog);
            page.Controls.Add(root);
        }

        void RunFollow(string[] files)
        {
            followLog.Tag = true;   // 有内容后切换语言不再重写提示
            foreach (var f in files)
            {
                followLog.AppendText("\r\n==== " + Path.GetFileName(f) + "\r\n");
                if (!f.EndsWith(".pmm", StringComparison.OrdinalIgnoreCase))
                {
                    followLog.AppendText(L.T("跳过：不是 .pmm 文件", "スキップ：.pmm ファイルではありません", "Skipped: not a .pmm file") + "\r\n"); continue;
                }
                try { followLog.AppendText(Norm(FollowBinder.Process(f, rbAll.Checked))); }
                catch (Exception ex) { followLog.AppendText(L.T("失败：", "失敗：", "Failed: ") + ex.Message + "\r\n"); }
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            L.Load();
#if CLI_TEST
            Console.OutputEncoding = Encoding.UTF8;
            CliTest.Run(args);
            return;
#endif
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
        }
    }

#if CLI_TEST
    static class CliTest
    {
        // [--lang=0|1|2] follow [--all] a.pmm ...  |  merge 目标.pmm 相机骨.pmm 输出.pmm [--keep-camera] [--only=模型名]
        public static void Run(string[] a)
        {
            try
            {
                var lang = a.FirstOrDefault(x => x.StartsWith("--lang="));
                if (lang != null) L.Cur = int.Parse(lang.Substring(7));
                a = a.Where(x => !x.StartsWith("--lang=")).ToArray();
                if (a[0] == "follow")
                {
                    bool all = a.Contains("--all");
                    foreach (var f in a.Skip(1).Where(x => !x.StartsWith("--"))) Console.Write(FollowBinder.Process(f, all));
                }
                else if (a[0] == "parse")
                {
                    foreach (var f in a.Skip(1))
                    {
                        var p = PmmFile.Load(f);
                        Console.WriteLine(Path.GetFileName(f) + ": " + p.Models.Count + " models, " + p.CameraKeys.Count
                            + " camera keys, ID " + string.Join(",", p.Models.Select(m => m.Id)));
                    }
                }
                else if (a[0] == "merge")
                {
                    var render = PmmFile.Load(a[1]); var rig = PmmFile.Load(a[2]);
                    var names = new HashSet<string>(render.Models.Select(m => m.Name));
                    var only = a.FirstOrDefault(x => x.StartsWith("--only="));
                    var ids = new HashSet<int>(rig.Models.Where(m => only != null ? m.Name == only.Substring(7) : (!names.Contains(m.Name) && m.LooksLikeCameraRig())).Select(m => m.Id));
                    bool cam = !a.Contains("--keep-camera");
                    var refs = Merger.CollectRefs(render, rig, ids, cam);
                    foreach (var r in refs)
                        Console.WriteLine("ref " + r.SrcModelName + " / " + r.SrcBoneName + " | "
                            + string.Join("; ", r.Users.Select(u => u.Who + " " + u.Range + " (" + u.KeyCount + ")"))
                            + " | " + r.Match + " -> " + render.Describe(r.Target.Model, r.Target.Bone));
                    var ov = new Dictionary<int, string>();
                    foreach (var m in rig.Models.Where(x => ids.Contains(x.Id)))
                    {
                        var hit = Merger.ResolveModelPath(m, new[] { Path.GetDirectoryName(a[2]), Path.GetDirectoryName(a[1]) }.Concat(a.Where(x => x.StartsWith("--search=")).Select(x => x.Substring(9))));
                        if (hit != null) ov[m.Id] = hit;
                    }
                    Console.Write(Merger.Merge(render, rig, ids, cam, refs, a[3], ov));
                }
            }
            catch (Exception ex) { Console.WriteLine("ERROR: " + ex.Message); }
        }
    }
#endif
}
