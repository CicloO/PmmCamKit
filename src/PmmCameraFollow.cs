// PMM 相机工具（Cat Toolbox）：合并相机骨 / 相机追从批量绑定
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace CatToolbox
{
    class ModelItem
    {
        public int Id; public string Text;
        public override string ToString() { return Text; }
    }

    class MainForm : Form
    {
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

        // ---- 追从页 ----
        readonly RadioButton rbForward = new RadioButton(), rbAll = new RadioButton();
        readonly TextBox followLog = new TextBox();

        public MainForm(string[] initial)
        {
            Text = "PMM 相机工具 — Cat Toolbox";
            Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(820, 640);
            MinimumSize = new Size(640, 520);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var tMerge = new TabPage("合并相机骨");
            var tFollow = new TabPage("追从批量绑定");
            BuildMergePage(tMerge);
            BuildFollowPage(tFollow);
            tabs.TabPages.Add(tMerge);
            tabs.TabPages.Add(tFollow);
            Controls.Add(tabs);

            // 启动参数：--render 渲染.pmm --rig 相机骨.pmm 预填合并页；其余 .pmm 参数交给追从页处理
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

        static TextBox MakeLog(TextBox t)
        {
            t.Multiline = true; t.ReadOnly = true; t.ScrollBars = ScrollBars.Vertical;
            t.Dock = DockStyle.Fill; t.BackColor = SystemColors.Window;
            return t;
        }

        // ===================== 合并相机骨 =====================
        void BuildMergePage(TabPage page)
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));

            var tip = new Label
            {
                AutoSize = true, MaximumSize = new Size(780, 0),
                Text = "把「相机骨 PMM」里的相机骨模型（含骨骼关键帧）和相机移入「渲染 PMM」。"
                     + "移入的模型接在模型列表末尾，渲染 PMM 原有内容不变；原文件不会被修改。"
            };
            root.Controls.Add(tip);

            var files = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true };
            files.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            files.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            files.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            AddFileRow(files, "渲染 PMM：", tbRender, true);
            AddFileRow(files, "相机骨 PMM：", tbRig, true);
            AddFileRow(files, "输出：", tbOut, false);
            root.Controls.Add(files);

            var g1 = new GroupBox { Text = "要移入的模型（默认勾选渲染 PMM 里没有的模型；找不到的模型文件会自动按文件名搜索）", Dock = DockStyle.Fill };
            lbModels.Dock = DockStyle.Fill; lbModels.CheckOnClick = true; lbModels.HorizontalScrollbar = true;
            lbModels.ItemCheck += (s, e) => BeginInvoke(new Action(RefreshRefs));
            var pick = new Button { Text = "指定所选模型的文件…", Dock = DockStyle.Bottom, Height = 28 };
            pick.Click += (s, e) => PickModelFile();
            g1.Controls.Add(lbModels);
            g1.Controls.Add(pick);
            root.Controls.Add(g1);

            cbCamera.Text = "用相机骨 PMM 的相机替换渲染 PMM 的相机";
            cbCamera.AutoSize = true; cbCamera.Checked = true;
            cbCamera.CheckedChanged += (s, e) => RefreshRefs();
            root.Controls.Add(cbCamera);

            var g2 = new GroupBox { Text = "绑定替换（相机骨模型的外部亲、相机追从所引用的模型/骨骼 → 渲染 PMM 中的目标）", Dock = DockStyle.Fill };
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "相机骨 PMM 中的引用", ReadOnly = true, FillWeight = 34 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "被谁引用", ReadOnly = true, FillWeight = 20 });
            grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "目标模型", FillWeight = 26, FlatStyle = FlatStyle.Flat });
            grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "目标骨骼", FillWeight = 20, FlatStyle = FlatStyle.Flat });
            grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellValueChanged += GridValueChanged;
            grid.DataError += (s, e) => { e.ThrowException = false; };
            g2.Controls.Add(grid);
            root.Controls.Add(g2);

            var btn = new Button { Text = "合并", Width = 120, Height = 30 };
            btn.Click += (s, e) => RunMerge();
            root.Controls.Add(btn);

            root.Controls.Add(MakeLog(mergeLog));
            page.Controls.Add(root);
        }

        void AddFileRow(TableLayoutPanel t, string label, TextBox tb, bool open)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 4, 0) });
            tb.Dock = DockStyle.Fill;
            t.Controls.Add(tb);
            var b = new Button { Text = "浏览…", Width = 70 };
            b.Click += (s, e) =>
            {
                if (open)
                {
                    using (var d = new OpenFileDialog { Filter = "MMD 工程文件 (*.pmm)|*.pmm" })
                        if (d.ShowDialog(this) == DialogResult.OK) { tb.Text = d.FileName; LoadFiles(); }
                }
                else
                {
                    using (var d = new SaveFileDialog { Filter = "MMD 工程文件 (*.pmm)|*.pmm", FileName = Path.GetFileName(tb.Text) })
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
                    tbOut.Text = Path.Combine(Path.GetDirectoryName(tbRender.Text),
                        Path.GetFileNameWithoutExtension(tbRender.Text) + "_合并.pmm");
                    mergeLog.AppendText("渲染 PMM：模型 " + render.Models.Count + " 个，相机关键帧 " + render.CameraKeys.Count + " 个\r\n");
                }
                if (File.Exists(tbRig.Text))
                {
                    rig = PmmFile.Load(tbRig.Text);
                    mergeLog.AppendText("相机骨 PMM：模型 " + rig.Models.Count + " 个，相机关键帧 " + rig.CameraKeys.Count + " 个\r\n");
                }
            }
            catch (Exception ex) { mergeLog.AppendText("读取失败：" + ex.Message + "\r\n"); }
            if (render == null || rig == null) return;

            loading = true;
            pathOverride.Clear();
            var names = new HashSet<string>(render.Models.Select(m => m.Name));
            var dirs = new[] { Path.GetDirectoryName(rig.FilePath), Path.GetDirectoryName(render.FilePath) };
            foreach (var m in rig.Models)
            {
                if (!names.Contains(m.Name) && !File.Exists(m.Path))
                {
                    string hit = Merger.ResolveModelPath(m, dirs);
                    if (hit != null) pathOverride[m.Id] = hit;
                }
                lbModels.Items.Add(new ModelItem { Id = m.Id, Text = ItemText(m, names.Contains(m.Name)) }, !names.Contains(m.Name));
            }
            loading = false;
            RefreshRefs();
        }

        string ItemText(PmmModel m, bool dup)
        {
            string path; bool ov = pathOverride.TryGetValue(m.Id, out path);
            if (!ov) path = m.Path;
            string st = File.Exists(path) ? (ov ? "  ✔ 已重新定位：" + path : "") : "  ⚠ 找不到模型文件：" + path;
            return m.Name + "（骨骼 " + m.Bones.Count + "）" + (dup ? "  —— 渲染 PMM 里已有同名模型" : "") + st;
        }

        void PickModelFile()
        {
            if (rig == null || lbModels.SelectedIndex < 0) { mergeLog.AppendText("请先在列表里选中一个模型" + Environment.NewLine); return; }
            var it = (ModelItem)lbModels.SelectedItem;
            var m = rig.ById(it.Id);
            using (var d = new OpenFileDialog { Filter = "MMD 模型 (*.pmx;*.pmd)|*.pmx;*.pmd", Title = "指定「" + m.Name + "」的模型文件" })
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
            var modelCol = (DataGridViewComboBoxColumn)grid.Columns[2];
            modelCol.Items.Clear();
            modelCol.Items.Add("无（解除）");
            foreach (var m in render.Models) modelCol.Items.Add(Label(m));
            foreach (var r in refs)
            {
                int i = grid.Rows.Add(rig.Describe(r.SrcModel, r.SrcBone), r.UsedBy, null, null);
                SetRowTarget(grid.Rows[i], r);
            }
            loading = false;
        }

        // 下拉框里显示「名字 [ID n]」：同名模型（如两个同名角色）也能区分
        static string Label(PmmModel m) { return m.Name + "  [ID " + m.Id + "]"; }

        void SetRowTarget(DataGridViewRow row, ExternalRef r)
        {
            var tm = r.Target.Model < 0 ? null : render.ById(r.Target.Model);
            row.Cells[2].Value = tm == null ? "无（解除）" : Label(tm);
            var boneCell = (DataGridViewComboBoxCell)row.Cells[3];
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
            if (e.ColumnIndex == 2)
            {
                string label = row.Cells[2].Value as string;
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
            else if (e.ColumnIndex == 3 && r.Target.Model >= 0)
            {
                var tm = render.ById(r.Target.Model);
                int bi = tm.Bones.IndexOf(row.Cells[3].Value as string);
                if (bi >= 0) r.Target.Bone = bi;
            }
            loading = false;
        }

        void RunMerge()
        {
            LoadFiles();
            if (render == null || rig == null) { mergeLog.AppendText("请先选择渲染 PMM 和相机骨 PMM\r\n"); return; }
            if (string.Equals(Path.GetFullPath(tbOut.Text), Path.GetFullPath(tbRender.Text), StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFullPath(tbOut.Text), Path.GetFullPath(tbRig.Text), StringComparison.OrdinalIgnoreCase))
            {
                mergeLog.AppendText("输出文件不能和输入文件相同\r\n"); return;
            }
            var unbound = refs.Where(r => r.Target.Model < 0).ToList();
            if (unbound.Count > 0 && MessageBox.Show(this,
                    "以下引用没有对应目标，合并后会解除绑定：\r\n" + string.Join("\r\n", unbound.Select(r => rig.Describe(r.SrcModel, r.SrcBone))) + "\r\n\r\n继续吗？",
                    "确认", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            if (File.Exists(tbOut.Text) && MessageBox.Show(this, "输出文件已存在，覆盖吗？\r\n" + tbOut.Text, "确认",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            try
            {
                mergeLog.AppendText("\r\n==== 合并\r\n" + Norm(Merger.Merge(render, rig, CheckedIds(), cbCamera.Checked, refs, tbOut.Text, pathOverride)));
            }
            catch (Exception ex) { mergeLog.AppendText("合并失败：" + ex.Message + "\r\n"); }
        }

        static string Norm(string s) { return s.Replace("\r\n", "\n").Replace("\n", "\r\n"); }

        // ===================== 追从批量绑定 =====================
        void BuildFollowPage(TabPage page)
        {
            page.AllowDrop = true;
            var tip = new Label
            {
                Text = "用法：在 MMD 里给第一个相机关键帧设置「骨骼追从」并保存 → 在这里选择或拖入 PMM。\r\n"
                     + "结果另存为「原名_追従.pmm」放在原文件旁边，原文件不会被修改。",
                Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 8, 8, 0)
            };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 4, 6, 0) };
            var btn = new Button { Text = "选择 PMM…", Width = 110, Height = 28 };
            btn.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog { Filter = "MMD 工程文件 (*.pmm)|*.pmm", Multiselect = true })
                    if (dlg.ShowDialog(this) == DialogResult.OK) RunFollow(dlg.FileNames);
            };
            rbForward.Text = "向后填充（中途可手动换目标）";
            rbForward.AutoSize = true; rbForward.Checked = true; rbForward.Margin = new Padding(12, 7, 0, 0);
            rbAll.Text = "全部覆盖";
            rbAll.AutoSize = true; rbAll.Margin = new Padding(12, 7, 0, 0);
            bar.Controls.Add(btn); bar.Controls.Add(rbForward); bar.Controls.Add(rbAll);
            MakeLog(followLog).Text = "把 PMM 文件拖到这里，或点「选择 PMM…」。\r\n";
            followLog.AllowDrop = true;
            DragEventHandler enter = (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragEventHandler drop = (s, e) => RunFollow((string[])e.Data.GetData(DataFormats.FileDrop));
            page.DragEnter += enter; page.DragDrop += drop;
            followLog.DragEnter += enter; followLog.DragDrop += drop;
            page.Controls.Add(followLog);
            page.Controls.Add(bar);
            page.Controls.Add(tip);
        }

        void RunFollow(string[] files)
        {
            foreach (var f in files)
            {
                followLog.AppendText("\r\n==== " + Path.GetFileName(f) + "\r\n");
                if (!f.EndsWith(".pmm", StringComparison.OrdinalIgnoreCase)) { followLog.AppendText("跳过：不是 .pmm 文件\r\n"); continue; }
                try { followLog.AppendText(Norm(FollowBinder.Process(f, rbAll.Checked))); }
                catch (Exception ex) { followLog.AppendText("失败：" + ex.Message + "\r\n"); }
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
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
        // follow [--all] a.pmm ...  |  merge 渲染.pmm 相机骨.pmm 输出.pmm [--keep-camera]
        public static void Run(string[] a)
        {
            try
            {
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
                        Console.WriteLine(Path.GetFileName(f) + "：模型 " + p.Models.Count + "，相机关键帧 " + p.CameraKeys.Count
                            + "，ID " + string.Join(",", p.Models.Select(m => m.Id)));
                    }
                }
                else if (a[0] == "merge")
                {
                    var render = PmmFile.Load(a[1]); var rig = PmmFile.Load(a[2]);
                    var names = new HashSet<string>(render.Models.Select(m => m.Name));
                    var only = a.FirstOrDefault(x => x.StartsWith("--only="));
                    var ids = new HashSet<int>(rig.Models.Where(m => only != null ? m.Name == only.Substring(7) : !names.Contains(m.Name)).Select(m => m.Id));
                    bool cam = !a.Contains("--keep-camera");
                    var refs = Merger.CollectRefs(render, rig, ids, cam);
                    foreach (var r in refs)
                        Console.WriteLine("引用 " + rig.Describe(r.SrcModel, r.SrcBone) + "（" + r.UsedBy + "）→ 默认 " + render.Describe(r.Target.Model, r.Target.Bone));
                    var ov = new Dictionary<int, string>();
                    foreach (var m in rig.Models.Where(x => ids.Contains(x.Id)))
                    {
                        var hit = Merger.ResolveModelPath(m, new[] { Path.GetDirectoryName(a[2]), Path.GetDirectoryName(a[1]) }.Concat(a.Where(x => x.StartsWith("--search=")).Select(x => x.Substring(9))));
                        if (hit != null) ov[m.Id] = hit;
                    }
                    Console.Write(Merger.Merge(render, rig, ids, cam, refs, a[3], ov));
                }
            }
            catch (Exception ex) { Console.WriteLine("失败：" + ex.Message); }
        }
    }
#endif
}
